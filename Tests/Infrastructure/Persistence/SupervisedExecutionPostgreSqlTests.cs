using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Supervisor_Normal_PublicManual_CopyProvider(bool publicContext, bool provider)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, publicContext, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var calls = SupervisionProbe();
        var copies = new Copies(); var client = new ConsumingClient(); var entered = Signal(); var release = Signal();
        async Task Pause() { copies.AfterCopy = null; entered.TrySetResult(); await release.Task.WaitAsync(Token); }
        if (provider) client.DuringRead = Pause; else copies.AfterCopy = Pause;
        var running = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            Assert.Equal(0, calls.Renewals); var job = await CombinedJob();
            Assert.Equal(1, job.AttemptCount);
            clock.Advance(TimeSpan.FromSeconds(30));
            var renewal = await SupervisionRead(calls.Renewed);
            Assert.Equal(LeaseOperationState.Renewed, renewal.State); Assert.True(renewal.Receipt!.IsEffectiveExtension);
            Assert.Equal(job.ProcessingAttemptId, calls.Inputs.Single().ExpectedProcessingAttemptId);
            Assert.Equal(job.LeaseExpiresAtUtc, calls.Inputs.Single().ExpectedLeaseExpiresAtUtc);
            Assert.False(running.IsCompleted); Assert.All(copies.Created, c => Assert.False(c.Disposed));
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
        }
        finally { release.TrySetResult(); }
        var result = await running;
        Assert.Equal(AssignedSupervisionOutcome.CoordinatorCompleted, result.Outcome);
        Assert.Equal(AssignedSupervisionStopReason.None, result.StopReason); Assert.False(result.CoordinatorCancelled);
        Assert.Equal(RequirementProcessingJobState.Succeeded, result.Observation!.Snapshot!.JobState);
        await CombinedPublished(new(seed, request, result.Claim!.Receipt!), result.Coordinator!, files, copies, client, 1);
        Assert.Equal(1, calls.Claims); Assert.Equal(1, calls.Renewals); Assert.Equal(1, calls.MaximumActive); Assert.Equal(0, calls.ActiveRenewals);
        clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(1, calls.Renewals);
    }

    [Fact]
    public async Task Supervisor_NoExtension_OneOperationPerOpportunity()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var calls = SupervisionProbe();
        var copies = new Copies(); var client = new ConsumingClient(); var entered = Signal(); var release = Signal();
        client.DuringRead = async () => { entered.TrySetResult(); await release.Task.WaitAsync(Token); };
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            await SupervisionRead(clock.Scheduled);
            await using (var db = fixture.CreateDbContext())
            {
                var horizon = (await H.Now()).AddMinutes(10);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={horizon} WHERE id={request.JobId}", Token);
            }
            clock.Advance(TimeSpan.FromSeconds(30));
            var first = await SupervisionRead(calls.Renewed);
            Assert.Equal(LeaseOperationState.Renewed, first.State); Assert.False(first.Receipt!.IsEffectiveExtension);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            clock.Advance(TimeSpan.FromSeconds(29)); Assert.Equal(1, calls.Renewals);
            clock.Advance(TimeSpan.FromSeconds(1));
            var second = await SupervisionRead(calls.Renewed);
            Assert.False(second.Receipt!.IsEffectiveExtension); Assert.NotEqual(first.Receipt.OperationId, second.Receipt.OperationId);
            Assert.Equal(first.Receipt.ExpiresAtUtc, second.Receipt.ExpiresAtUtc);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            Assert.Equal(2, calls.Renewals); Assert.Equal(1, calls.MaximumActive);
        }
        finally { release.TrySetResult(); }
        var result = await task;
        await CombinedPublished(new(seed, request, result.Claim!.Receipt!), result.Coordinator!, files, copies, client, 2);
    }

    [Fact]
    public async Task Supervisor_LateStarted_NoTicksBeforeConfirmation_NoCatchUp()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var clock = new ManualSupervisionClock(); var calls = SupervisionProbe(); var copies = new Copies(); var client = new ConsumingClient();
        var startReached = Signal(); var startRelease = Signal(); var providerReached = Signal(); var providerRelease = Signal();
        client.DuringRead = async () => { providerReached.TrySetResult(); await providerRelease.Task.WaitAsync(Token); };
        var task = Supervisor(files, copies, client, clock, calls, start: [new Saved(async _ =>
        { startReached.TrySetResult(); await startRelease.Task.WaitAsync(Token); })]).ExecuteAsync(SupervisionRequest(seed), seed.Actor, Token);
        try
        {
            await startReached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            clock.Advance(TimeSpan.FromSeconds(95)); Assert.Equal(0, calls.Renewals); Assert.Equal(0, calls.Observations);
            Assert.False(clock.Scheduled.Reader.TryRead(out _)); startRelease.TrySetResult();
            await providerReached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(LeaseOperationState.Renewed, (await SupervisionRead(calls.Renewed)).State);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled)); Assert.Equal(1, calls.Renewals);
        }
        finally { startRelease.TrySetResult(); providerRelease.TrySetResult(); }
        Assert.Equal(AssignedExecutionOutcome.Published, (await task).Coordinator!.Outcome); Released(files, copies, client);
    }

    [Theory]
    [InlineData("claim-replay")] [InlineData("claim-rejected")] [InlineData("claim-uncertain")]
    [InlineData("start-replay")] [InlineData("start-uncertain")]
    public async Task Supervisor_NoResume_ClaimOrStart(string mode)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies(); var client = new ConsumingClient();
        var committed = false;
        IInterceptor[] lost = [new CommitHook(() => { committed = true; throw new IOException("response lost"); }),
            new OpenHook(() => { if (committed) throw new IOException("observer unavailable"); })];
        if (mode == "claim-replay") Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(request, seed.Actor, Token)).State);
        if (mode == "claim-rejected") request = request with { RequirementId = Guid.NewGuid() };
        IInterceptor[] claimHooks = mode == "claim-uncertain" ? lost : [];
        if (mode == "start-replay") claimHooks = [new SupervisionAfterCommit(async () =>
        {
            var job = await CombinedJob();
            var execution = seed.Execution with { LeaseOwner = job.LeaseOwner!, LeaseGeneration = job.LeaseGeneration };
            Assert.Equal(AssignedJobStartState.Started, (await H.StartService().StartVerifiedAsync(execution, seed.Actor, Token)).State);
        })];
        var calls = SupervisionProbe(claim: claimHooks);
        var result = await Supervisor(files, copies, client, clock, calls, start: mode == "start-uncertain" ? lost : null)
            .ExecuteAsync(request, seed.Actor, Token);
        Assert.Equal(1, calls.Claims); Assert.Equal(0, calls.Renewals); Assert.Equal(0, files.Opens); Assert.Equal(0, client.Calls);
        Assert.Empty(copies.Created); Assert.False(clock.Scheduled.Reader.TryRead(out _));
        if (mode.StartsWith("claim", StringComparison.Ordinal)) Assert.Null(result.Coordinator);
        if (mode == "claim-replay") Assert.Equal(LeaseOperationState.AlreadyApplied, result.Claim!.State);
        if (mode == "claim-uncertain") Assert.Equal(AssignedSupervisionOutcome.Indeterminate, result.Outcome);
        if (mode == "claim-rejected") Assert.Equal(AssignedSupervisionOutcome.NotStarted, result.Outcome);
        if (mode == "start-replay") Assert.Equal(AssignedExecutionOutcome.AlreadyStarted, result.Coordinator!.Outcome);
        if (mode == "start-uncertain") Assert.Equal(AssignedExecutionOutcome.Indeterminate, result.Coordinator!.Outcome);
        Released(files, copies, client);
    }

    [Fact]
    public async Task Supervisor_ConcurrentClaims_PostgreSqlExclusion_NoLoserResources()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var first = SupervisionRequest(seed); var second = SupervisionRequest(seed);
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); var release = Signal();
        var providerReached = Signal(); var providerRelease = Signal();
        var winnerCopies = new Copies(); var loserCopies = new Copies(); var client = new ConsumingClient();
        client.DuringRead = async () => { providerReached.TrySetResult(); await providerRelease.Task.WaitAsync(Token); };
        var gate = new SupervisionClaimGate(entered, release); var probe = new JobProtocolTestSupport.QuoteProbe();
        var winner = Supervisor(files, winnerCopies, client, new(), SupervisionProbe(claim: [gate])).ExecuteAsync(first, seed.Actor, Token);
        Task<AssignedSupervisionResult>? loser = null;
        try
        {
            var pid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            loser = Supervisor(files, loserCopies, client, new(), SupervisionProbe(claim: [probe])).ExecuteAsync(second, seed.Actor, Token);
            var blocked = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(pid, blocked); await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, pid);
            release.TrySetResult(); Assert.Equal(AssignedSupervisionOutcome.NotStarted, (await loser).Outcome);
            await providerReached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Empty(loserCopies.Created); Assert.All(winnerCopies.Created, c => Assert.False(c.Disposed)); Assert.Equal(1, client.Calls);
            output.WriteLine($"Supervisor Claim winner PID {pid}, blocked PID {blocked}; pg_blocking_pids observed.");
        }
        finally { release.TrySetResult(); providerRelease.TrySetResult(); await winner; if (loser is not null) await loser; }
        Assert.Equal(AssignedExecutionOutcome.Published, (await winner).Coordinator!.Outcome); Released(files, winnerCopies, client);
    }
}
