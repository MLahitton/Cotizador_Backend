using Application.PublicExperience;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicPreparationVerifiedPostgreSqlTests
{
    [Fact]
    public async Task ExperienceEmission_CommitLostAfterRealClaim_RecognizesImmutableEmission()
    {
        using var files = await Seed();
        ExperienceAdmissionResult? admission = null;
        LeaseOperationResult? claim = null;
        Exception? callbackError = null;
        var completed = false;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        var fault = new CommitFailure(false, async () =>
        {
            try
            {
                await using var db = fixture.CreateDbContext();
                var job = await db.RequirementProcessingJobs.SingleAsync(Token);
                admission = await new ExperienceAdmissionService(new ExperienceAuthorizationStore(options)).AdmitAsync(job.Id, Token);
                var permit = admission.Permit!; var a = permit.Admission;
                claim = await new InitialLeaseClaimStore(options).ClaimInitialV2Async(
                    new(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit }, a.ActorUserId, Token);
                completed = true;
            }
            catch (Exception exception) { callbackError = exception; throw; }
        });
        await using var caller = Context(fault);
        var result = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.Null(callbackError); Assert.True(completed);
        Assert.Equal(ExperienceAdmissionState.Admitted, admission!.State);
        Assert.Equal(LeaseOperationState.Claimed, claim!.State);
        Assert.True(result!.IsSuccess); Assert.False(result.IsReplay);
        await using var read = fixture.CreateDbContext();
        var jobAfter = await read.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, jobAfter.State);
        Assert.NotNull(jobAfter.DispatchAdmittedAtUtc); Assert.Single(await read.JobLeaseOperations.ToListAsync(Token));
        var before = await EmissionSnapshot();
        Assert.True((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsReplay);
        Assert.Equal(before, await EmissionSnapshot());
        Assert.Equal(1, files.Saves);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ExperienceEmission_CommitLost_IndependentObservationAndOriginalCaller(bool cancelCaller, bool unavailable)
    {
        using var files = await Seed();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var release = Signal();
        var observation = new ObservationGate(release.Task, unavailable);
        string? committed = null;
        var fault = new CommitFailure(false, async () => { committed = await EmissionSnapshot(); observation.Enabled = true; });
        await using var caller = Context(fault, observation);
        var task = EmissionService(caller, files).ExecuteAsync("public-test-id", cancel.Token);
        try
        {
            await observation.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(fault.CommittedContextId, observation.ContextId);
            Assert.NotEqual(cancel.Token, observation.ObservationToken);
            if (cancelCaller) cancel.Cancel();
            Assert.False(observation.ObservationToken.IsCancellationRequested);
            release.TrySetResult();
            var exception = await Record.ExceptionAsync(() => task);
            if (cancelCaller) Assert.Equal(cancel.Token, Assert.IsType<OperationCanceledException>(exception).CancellationToken);
            else { Assert.Null(exception); Assert.True((await task)!.IsSuccess); }
            Assert.NotNull(committed); Assert.Equal(committed, await EmissionSnapshot());
            await using var read = fixture.CreateDbContext();
            Assert.Single(await read.RequirementProcessingJobs.ToListAsync(Token));
            Assert.Single(await read.Requirements.ToListAsync(Token));
            Assert.Equal(1, files.Opens); Assert.Equal(1, files.Saves); Assert.Equal(0, files.Deletes);
            observation.Enabled = false;
            Assert.True((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsReplay);
            Assert.Equal(committed, await EmissionSnapshot());
        }
        finally { release.TrySetResult(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData("missing")] [InlineData("different")]
    public async Task ExperienceEmission_UnknownCandidateIsNotRecognizedOrRepaired(string mutation)
    {
        using var files = await Seed();
        string? committed = null;
        var fault = new CommitFailure(false, async () =>
        {
            await using var edit = fixture.CreateDbContext();
            if (mutation == "missing") await edit.RequirementProcessingJobs.ExecuteDeleteAsync(Token);
            else await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET execution_request_id={Guid.NewGuid()}", Token);
            committed = await EmissionSnapshot();
        });
        await using var caller = Context(fault);
        await Assert.ThrowsAsync<IOException>(() => EmissionService(caller, files).ExecuteAsync("public-test-id", Token));
        Assert.True(fault.Injected); Assert.NotNull(committed); Assert.Equal(committed, await EmissionSnapshot());
        Assert.Equal(1, files.Saves); Assert.Equal(0, files.Deletes);
    }

    [Fact]
    public async Task ExperienceEmission_ConcurrentPreparations_OneIntentAndObservedBlocking()
    {
        using var files = await Seed();
        var other = new Files(files.Root, output);
        var aCopied = Signal(); var bCopied = Signal(); var aContinue = Signal(); var bContinue = Signal(); var commit = Signal();
        var gate = new CommitGate(commit.Task); var probe = new SubmissionProbe();
        files.AfterSave = async () => { aCopied.TrySetResult(); await aContinue.Task.WaitAsync(Token); };
        other.AfterSave = async () => { bCopied.TrySetResult(); await bContinue.Task.WaitAsync(Token); probe.Enabled = true; };
        await using var aDb = Context(gate); await using var bDb = Context(probe);
        var a = EmissionService(aDb, files).ExecuteAsync("public-test-id", Token);
        var b = EmissionService(bDb, other).ExecuteAsync("public-test-id", Token);
        try
        {
            await Task.WhenAll(aCopied.Task, bCopied.Task).WaitAsync(TimeSpan.FromSeconds(20), Token);
            aContinue.TrySetResult();
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            bContinue.TrySetResult();
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Emission winner={winner}, blocked={loser}; pg_blocking_pids observed before release.");
            commit.TrySetResult();
            var first = await a; var second = await b;
            Assert.True(first!.IsSuccess); Assert.False(first.IsReplay);
            Assert.True(second!.IsSuccess); Assert.True(second.IsReplay);
            Assert.Equal(first.RequirementId, second.RequirementId);
            await using var read = fixture.CreateDbContext();
            var job = Assert.Single(await read.RequirementProcessingJobs.ToListAsync(Token));
            Assert.Equal(first.RequirementId, job.RequirementId);
            Assert.Single(await read.Requirements.ToListAsync(Token));
            Assert.Single(await read.RequirementFiles.ToListAsync(Token));
            Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
            Assert.Equal(0, files.Deletes + other.Deletes);
        }
        finally { aContinue.TrySetResult(); bContinue.TrySetResult(); commit.TrySetResult(); await Task.WhenAll(a, b); }
    }
}
