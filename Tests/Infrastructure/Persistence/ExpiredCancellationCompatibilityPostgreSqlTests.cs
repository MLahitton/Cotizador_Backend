using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.ProcessRequirement;
using System.Data.Common;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class ExpiredReadGate : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = Signal();
        private bool held;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!held && command.CommandText.Contains("core.pre_quotes") && command.CommandText.Contains("FOR UPDATE"))
            {
                held = true; Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredB_PublicationAndClosure_LegalOrders(bool publishFirst)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var copies = new Copies(); var reached = Signal(); var release = Signal();
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); } };
        var gate = new ExpiredGate(); var requestProbe = new JobProtocolTestSupport.QuoteProbe();
        var running = Coordinator(files, copies, client, publication: publishFirst ? [gate] : null).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token);
        Task<JobTerminationResult>? request = null; string? closed = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            if (publishFirst)
            {
                release.TrySetResult(); var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                request = H.Call("request", await WithAttempt(s.Seed), H.Service(requestProbe));
                var loser = await requestProbe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
                output.WriteLine($"B publish-before-Request PID={winner}/{loser}, pg_blocking_pids observed.");
                gate.Release.TrySetResult(); Assert.Equal(JobTerminationFailure.TerminalConflict, (await request).Failure);
                Assert.Equal(AssignedExecutionOutcome.Published, (await running).Outcome);
            }
            else
            {
                var seed = await WithAttempt(s.Seed);
                Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", seed)).State);
                await using var db = fixture.CreateDbContext(); var expiry = s.Receipt.AssignedAtUtc.AddTicks(10);
                Assert.True(await H.Now() > expiry);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry}", Token);
                var job = await CombinedJob(); var e = new ExpiredCancellation(new(job.Id, job.RequirementId, job.ExecutionRequestId, s.Receipt.OperationId),
                    Guid.NewGuid(), s.Receipt.LeaseOwner, 1, s.Receipt.AssignedAtUtc, job.ProcessingAttemptId, job.CancellationRequestedAtUtc!.Value, expiry);
                Assert.Equal(JobTerminationState.CancellationConfirmed, (await ExpiredCloser().CloseExpiredCancellationAsync(e, seed.Actor, Token)).Result.State);
                closed = await ExpiredSnapshot(); Assert.False(running.IsCompleted); Assert.All(copies.Created, x => Assert.False(x.Disposed));
                var renew = await CombinedRenew().RenewActiveV2Async(new(job.Id, job.RequirementId, job.ExecutionRequestId, Guid.NewGuid(),
                    s.Receipt.OperationId, e.LeaseOwner, 1, expiry, job.ProcessingAttemptId), seed.Actor, Token);
                Assert.Equal(LeaseOperationFailure.Terminal, renew.Failure); Assert.Null(renew.Receipt);
            }
        }
        finally { release.TrySetResult(); gate.Release.TrySetResult(); await running; if (request is not null) await request; }
        var result = await running;
        if (!publishFirst)
        {
            Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome); Assert.Equal(AssignedExecutionStage.Publication, result.Stage);
            Assert.Equal(JobPublicationFailure.TerminalConflict, result.Publication!.Failure); Assert.Null(result.Termination);
            Assert.Equal(closed, await ExpiredSnapshot());
        }
        else
        {
            var job = await CombinedJob(); var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
            var e = new ExpiredCancellation(new(job.Id, job.RequirementId, job.ExecutionRequestId, s.Receipt.OperationId), Guid.NewGuid(),
                s.Receipt.LeaseOwner, 1, s.Receipt.AssignedAtUtc, job.ProcessingAttemptId, s.Receipt.AssignedAtUtc, s.Receipt.ExpiresAtUtc);
            ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token), JobTerminationFailure.TerminalConflict);
            Assert.Equal(before, await ExpiredSnapshot()); Assert.Equal(0, writes.Writes);
        }
        Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredB_RenewWithRequest_BothLockOrders(bool closeFirst)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        var e = s.Input;
        var renewInput = new RenewActiveLease(e.Identity.JobId, e.Identity.RequirementId, e.Identity.ExecutionRequestId,
            Guid.NewGuid(), e.Identity.ClaimOperationId, e.LeaseOwner, 1, e.ExpectedLeaseExpiresAtUtc, e.ExpectedProcessingAttemptId);
        var gate = new ExpiredGate(); var probe = new JobProtocolTestSupport.QuoteProbe();
        var readGate = new ExpiredReadGate();
        Task<ExpiredClosureResult>? close = null; Task<LeaseOperationResult>? renew = null;
        try
        {
            if (closeFirst)
            {
                close = ExpiredCloser(gate).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token);
                var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                renew = CombinedRenew(probe).RenewActiveV2Async(renewInput, s.Seed.Actor, Token);
                var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
                output.WriteLine($"B/Renew close-first PID={winner}/{loser}, pg_blocking_pids observed.");
                gate.Release.TrySetResult(); Assert.Equal(LeaseOperationFailure.Terminal, (await renew).Failure);
            }
            else
            {
                renew = CombinedRenew(readGate).RenewActiveV2Async(renewInput, s.Seed.Actor, Token);
                var winner = await readGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                close = ExpiredCloser(probe).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token);
                var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                Assert.NotEqual(winner, loser);
                await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
                output.WriteLine($"B/Renew renewal-first PID={winner}/{loser}, pg_blocking_pids observed.");
                readGate.Release.TrySetResult(); Assert.Equal(LeaseOperationFailure.CancellationRequested, (await renew).Failure);
            }
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await close!).Result.State);
        }
        finally { gate.Release.TrySetResult(); readGate.Release.TrySetResult(); if (close is not null) await close; if (renew is not null) await renew; }
        await using var read = fixture.CreateDbContext(); Assert.Equal(0, await read.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
    }

    [Fact]
    public async Task ExpiredB_RenewBeforeRequest_StaleExpiryNotAdopted()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, request: false, expired: false);
        var e = s.Input;
        var renewed = await CombinedRenew().RenewActiveV2Async(new(e.Identity.JobId, e.Identity.RequirementId, e.Identity.ExecutionRequestId,
            Guid.NewGuid(), e.Identity.ClaimOperationId, e.LeaseOwner, 1, e.ExpectedLeaseExpiresAtUtc, e.ExpectedProcessingAttemptId), s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Renewed, renewed.State); Assert.True(renewed.Receipt!.IsEffectiveExtension);
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s.Seed)).State);
        var job = await CombinedJob(); e = e with { ExpectedCancellationRequestedAtUtc = job.CancellationRequestedAtUtc!.Value };
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token), JobTerminationFailure.LeaseConflict);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
    }

    [Theory]
    [InlineData("project")] [InlineData("client")] [InlineData("ambiguous")]
    public async Task ExpiredB_ReplacementDoesNotInheritClosureException(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, attempt: false);
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "project") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now());
            if (mode == "client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
            await db.SaveChangesAsync(Token);
        }
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await ExpiredCloser().CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token)).Result.State);
        if (mode == "ambiguous")
        {
            await using var db = fixture.CreateDbContext(); var t = await H.Now();
            var other = RequirementProcessingJob.Create(s.Input.Identity.RequirementId, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, t);
            other.RequestCancellation(t); db.Add(other); await db.SaveChangesAsync(Token);
        }
        var before = await ExpiredSnapshot();
        await using var write = fixture.CreateDbContext(); using var content = new MemoryStream("new"u8.ToArray());
        var result = await ExpiredReplacement(write, s.Seed.Actor, files).ReplaceRequirementAsync(new(s.Input.Identity.RequirementId,
            [new("next.pdf", "application/pdf", 3, content)]), Token);
        Assert.Equal(mode == "project" ? ManageRequirementDocumentsFailure.InactiveProject : mode == "client"
            ? ManageRequirementDocumentsFailure.InactiveClient : ManageRequirementDocumentsFailure.RequirementNotReplaceable, result.Failure);
        Assert.Equal(before, await ExpiredSnapshot());
    }
}
