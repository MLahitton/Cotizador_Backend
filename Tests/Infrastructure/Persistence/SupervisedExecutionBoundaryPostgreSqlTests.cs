using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData("indeterminate")] [InlineData("rollback")] [InlineData("stale")] [InlineData("attempt")]
    [InlineData("expired")] [InlineData("authority")] [InlineData("context")]
    [InlineData("unavailable")] [InlineData("unexpected")] [InlineData("request")]
    public async Task Supervisor_StopsItself_NoRetryOrFictitiousTerminal(string mode)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies();
        var reached = Signal(); var release = Signal(); var stopped = Signal(); var committed = false; var injected = false;
        IInterceptor[] hooks = mode switch
        {
            "indeterminate" => [new CommitHook(() => { committed = true; throw new IOException("lost"); }),
                new OpenHook(() => { if (committed) throw new IOException("observer offline"); })],
            "rollback" => [new RenewalAfterWrite(() => { injected = true; throw new IOException("precommit"); })],
            _ => []
        };
        var calls = SupervisionProbe(renew: hooks, observation: mode == "unavailable" ? [new OpenHook(() => throw new IOException("offline"))] : null);
        if (mode is "stale" or "attempt" or "unexpected") calls.AfterObservation = async _ =>
        {
            calls.AfterObservation = null; injected = true;
            if (mode == "unexpected") throw new IOException("unexpected after real observation");
            await using var db = fixture.CreateDbContext();
            if (mode == "stale")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=lease_expires_at_utc+interval '1 second' WHERE id={request.JobId}", Token);
            else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE requirement_id={request.RequirementId}", Token);
        };
        var client = new ConsumingClient { Action = async (r, ct) =>
        {
            using var registration = ct.Register(() => stopped.TrySetResult()); reached.TrySetResult();
            await release.Task.WaitAsync(Token); return ConsumingClient.Response(r);
        }};
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            await using (var db = fixture.CreateDbContext())
            {
                if (mode == "expired")
                {
                    var expiry = (await CombinedJob()).StartedAtUtc!.Value.AddTicks(10); Assert.True(await H.Now() > expiry);
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={request.JobId}", Token);
                }
                if (mode == "authority") (await db.Users.SingleAsync(Token)).Deactivate(await H.Now());
                if (mode == "context") (await db.Projects.SingleAsync(Token)).SetActive(false, seed.Actor, await H.Now());
                await db.SaveChangesAsync(Token);
            }
            if (mode == "request") Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(seed))).State);
            var before = await CombinedSnapshot();
            clock.Advance(TimeSpan.FromSeconds(30));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.False(task.IsCompleted); Assert.All(copies.Created, c => Assert.False(c.Disposed));
            var count = calls.Renewals; clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(count, calls.Renewals);
            Assert.False(clock.Scheduled.Reader.TryRead(out _)); Assert.Equal(1, client.Calls);
            if (mode is not ("indeterminate" or "stale" or "attempt")) Assert.Equal(before, await CombinedSnapshot());
        }
        finally { release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedSupervisionOutcome.Stopped, result.Outcome); Assert.True(result.CoordinatorCancelled); Assert.Null(result.Coordinator);
        var reason = mode switch
        {
            "indeterminate" or "rollback" or "stale" or "attempt" => AssignedSupervisionStopReason.RenewalNotConfirmed,
            "unavailable" => AssignedSupervisionStopReason.ObservationUnavailable,
            "unexpected" => AssignedSupervisionStopReason.UnexpectedCycleFailure,
            "request" => AssignedSupervisionStopReason.CancellationRequested,
            _ => AssignedSupervisionStopReason.IncompatibleObservation
        };
        Assert.Equal(reason, result.StopReason);
        if (mode == "indeterminate") { Assert.True(committed); Assert.Equal(LeaseOperationState.Indeterminate, result.LastRenewal!.State); }
        if (mode == "rollback") { Assert.True(injected); Assert.Equal(LeaseOperationState.PersistenceError, result.LastRenewal!.State); }
        if (mode == "stale") Assert.Equal(LeaseOperationFailure.StaleLease, result.LastRenewal!.Failure);
        if (mode == "attempt") Assert.Equal(LeaseOperationFailure.AttemptConflict, result.LastRenewal!.Failure);
        Assert.Equal(mode is "indeterminate" or "rollback" or "stale" or "attempt" ? 1 : 0, calls.Renewals);
        await using var read = fixture.CreateDbContext(); var job = await read.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.FinishedAtUtc); Assert.Null(job.LastErrorCode);
        Assert.Null(job.PublicationContentHash); Assert.Equal(mode == "request", job.CancellationRequestedAtUtc.HasValue);
        Assert.Equal(DocumentProcessingState.Processing, (await read.RequirementProcessingAttempts.SingleAsync(Token)).ProcessingState);
        Assert.Equal(RequirementStatus.Processing, (await read.Requirements.SingleAsync(Token)).Status);
        Assert.Empty(await read.RequirementTechnicalProposals.ToListAsync(Token)); Assert.Empty(await read.RequirementExtractionResults.ToListAsync(Token));
        Assert.Equal(mode == "indeterminate" ? 1 : 0, await read.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
        Assert.Single(await read.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Released(files, copies, client); await CombinedReceiptUnchanged(result.Claim!.Receipt!);
        output.WriteLine($"Supervisor Stop {mode}: automatic token cancellation, renew calls={calls.Renewals}, provider=1, Running preserved.");
    }

    [Fact]
    public async Task Supervisor_ReconciledRenew_AlreadyApplied_ContinuesWithoutRetry()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies();
        var reached = Signal(); var release = Signal(); var injected = false;
        var calls = SupervisionProbe(renew: [new CommitHook(() => { injected = true; throw new IOException("lost"); })]);
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); } };
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); var renewed = await SupervisionRead(calls.Renewed);
            Assert.True(injected); Assert.Equal(LeaseOperationState.AlreadyApplied, renewed.State);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled)); Assert.Equal(1, calls.Renewals);
        }
        finally { release.TrySetResult(); }
        var result = await task;
        await CombinedPublished(new(seed, request, result.Claim!.Receipt!), result.Coordinator!, files, copies, client, 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Supervisor_CallerCancellation_RenewOrReconciliation_JoinsAndKeepsCommit(bool reconcile)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reached = Signal(); var release = Signal(); var committed = false;
        var gate = new SupervisionOpenGate(() => !reconcile || committed);
        IInterceptor[] hooks = reconcile ? [new CommitHook(() => { committed = true; throw new IOException("lost"); }), gate] : [gate];
        var calls = SupervisionProbe(renew: hooks);
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); } };
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, caller.Token);
        var error = Assert.ThrowsAsync<OperationCanceledException>(() => task);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            caller.Cancel(); Assert.False(task.IsCompleted);
            if (reconcile) { Assert.True(committed); Assert.False(gate.ObservedToken.IsCancellationRequested); Assert.NotEqual(caller.Token, gate.ObservedToken); }
        }
        finally { gate.Release.TrySetResult(); release.TrySetResult(); }
        Assert.Equal(caller.Token, (await error).CancellationToken); Assert.Equal(0, calls.ActiveRenewals);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(reconcile ? 1 : 0, await db.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
        var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.CancellationRequestedAtUtc);
        Assert.Null(job.LastErrorCode); Assert.Null(job.PublicationContentHash); Released(files, copies, client);
    }
}
