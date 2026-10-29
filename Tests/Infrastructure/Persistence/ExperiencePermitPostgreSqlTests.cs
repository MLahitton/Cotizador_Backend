using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private async Task<DateTimeOffset> PermitWaitUntilExpired(DateTimeOffset expiry)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        while (true)
        {
            var now = new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(deadline.Token))!);
            if (now >= expiry) return now;
            await Task.Delay(20, deadline.Token);
        }
    }

    private async Task<(Seed Seed, ExperienceExecutionPermit Permit)> PermitSetup(Files files, double expirySeconds = 86400)
    {
        var s = await SeedFiles(files, publicContext: true, start: false, assigned: false);
        await using (var db = fixture.CreateDbContext())
        {
            var old = await db.RequirementProcessingJobs.SingleAsync(Token);
            var sid = old.SubmissionId!.Value;
            var correlation = old.CorrelationId;
            db.Remove(old);
            await db.SaveChangesAsync(Token);
            var now = (await H.Now()).AddSeconds(expirySeconds - 86400);
            var job = RequirementProcessingJob.CreateForAutomaticExperience(old.Id, old.RequirementId, sid,
                old.ExecutionRequestId, correlation, s.Actor, 1, now, now.AddHours(24));
            db.Add(job);
            await db.Set<PublicExperienceSubmissionFile>().Where(x => x.SubmissionId == sid).ExecuteDeleteAsync(Token);
            var rows = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(Token);
            for (var i = 0; i < rows.Count; i++)
            {
                var f = rows[i];
                db.Add(PublicExperienceSubmissionFile.CreateWithStoredEvidence(Guid.NewGuid(), sid, i,
                    f.OriginalFileName, f.ContentType, f.SizeBytes, $"source/{i}.pdf", f.StoredSha256!,
                    now, f.StoredLengthBytes!.Value, f.StoredEvidenceAtUtc!.Value, 1));
            }
            await db.SaveChangesAsync(Token);
        }
        var result = await new ExperienceAdmissionService(new ExperienceAuthorizationStore(H.Options())).AdmitAsync(s.Execution.JobId, Token);
        Assert.Equal(ExperienceAdmissionState.Admitted, result.State);
        return (s, Assert.IsType<ExperienceExecutionPermit>(result.Permit));
    }

    private CoordinateAssignedExecutionService PermitCoordinator(Files files, Copies copies, ConsumingClient client)
    {
        var documents = Store(files, copies);
        return new(new(new AssignedJobStartStore(H.Options())), documents, Consumer(files, copies, client),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()), documents),
            new(new JobPublicationStore(H.Options())), H.Service());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_Integrated_AdmissionRenewPublicationAndNoResume(bool provider)
    {
        await using var files = new Files();
        var (seed, permit) = await PermitSetup(files);
        var copies = new Copies(); var client = new ConsumingClient();
        var clock = new ManualSupervisionClock(); var calls = SupervisionProbe();
        var supervisor = new SuperviseAssignedExecutionService(new(calls), PermitCoordinator(files, copies, client),
            new(calls), new(calls), clock);
        var entered = Signal(); var release = Signal();
        async Task Pause() { copies.AfterCopy = null; entered.TrySetResult(); await release.Task.WaitAsync(Token); }
        if (provider) client.DuringRead = Pause; else copies.AfterCopy = Pause;
        var running = supervisor.ExecuteAsync(permit, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            clock.Advance(TimeSpan.FromSeconds(30));
            var renewal = await SupervisionRead(calls.Renewed);
            Assert.Equal(LeaseOperationState.Renewed, renewal.State);
            Assert.True(renewal.Receipt!.IsEffectiveExtension);
            Assert.Same(permit, calls.Inputs.Single().Permit);
            Assert.False(running.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var result = await running;
        Assert.Equal(AssignedSupervisionOutcome.CoordinatorCompleted, result.Outcome);
        var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId)
            { Permit = permit };
        await CombinedPublished(new(seed, request, result.Claim!.Receipt!), result.Coordinator!, files, copies, client, 1);
        var before = await CombinedSnapshot();
        var repeat = await supervisor.ExecuteAsync(permit, Token);
        Assert.Equal(AssignedSupervisionOutcome.NotStarted, repeat.Outcome);
        Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens);
        Assert.Null((await new ExperienceAdmissionService(new ExperienceAuthorizationStore(H.Options())).AdmitAsync(a.JobId, Token)).Permit);
        Released(files, copies, client);
    }
}
