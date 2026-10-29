using System.Data.Common;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Fact]
    public async Task Supervisor_StopAfterPublicationCommit_ObservedSucceededIsNotPublished()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies();
        var reached = Signal(); var release = Signal();
        var renewGate = new SupervisorRenewCommitGate(true); var publicationGate = new SupervisorRenewCommitGate(false);
        var calls = SupervisionProbe(renew: [renewGate,
            new OpenHook(() => { if (renewGate.Reached.Task.IsCompleted) throw new IOException("observer unavailable"); })]);
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); } };
        var supervisor = Supervisor(files, copies, client, clock, calls, publication: [publicationGate]);
        var task = supervisor.ExecuteAsync(request, seed.Actor, Token);
        string? committed = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); await renewGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            release.TrySetResult(); await publicationGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(RequirementProcessingJobState.Succeeded, (await CombinedJob()).State); committed = await CombinedSnapshot();
            renewGate.Release.TrySetResult(); await publicationGate.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.False(task.IsCompleted); Assert.Equal(1, client.Calls);
        }
        finally { release.TrySetResult(); renewGate.Release.TrySetResult(); publicationGate.Release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedSupervisionOutcome.Stopped, result.Outcome); Assert.True(result.CoordinatorCancelled); Assert.Null(result.Coordinator);
        Assert.Equal(LeaseOperationState.Indeterminate, result.LastRenewal!.State);
        Assert.Equal(RequirementProcessingJobState.Succeeded, result.Observation!.Snapshot!.JobState);
        Assert.Equal(committed, await CombinedSnapshot());
        var job = await CombinedJob(); Assert.Equal(PreparedJobPublication.Version2, job.PublicationContractVersion); Assert.NotNull(job.PublicationContentHash);
        await using var db = fixture.CreateDbContext(); Assert.Single(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Equal(RequirementStatus.Processed, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, (await db.RequirementProcessingAttempts.SingleAsync(Token)).Outcome);
        var replay = await supervisor.ExecuteAsync(request, seed.Actor, Token);
        Assert.Equal(AssignedSupervisionOutcome.NotStarted, replay.Outcome); Assert.Equal(LeaseOperationState.AlreadyApplied, replay.Claim!.State);
        Assert.Null(replay.Coordinator); Assert.Equal(1, client.Calls); Assert.Equal(1, calls.Renewals);
        Assert.Equal(committed, await CombinedSnapshot()); Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Supervisor_PublishedWhileRenewResponsePending_TerminalNotDegraded(bool lostResponse)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var request = SupervisionRequest(seed); var clock = new ManualSupervisionClock(); var copies = new Copies();
        var reached = Signal(); var release = Signal(); var gate = new SupervisorRenewCommitGate(lostResponse);
        var calls = SupervisionProbe(renew: [gate]);
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); } };
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(request, seed.Actor, Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            release.TrySetResult(); await gate.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(RequirementProcessingJobState.Succeeded, (await CombinedJob()).State);
            Assert.False(task.IsCompleted); Assert.Equal(1, calls.ActiveRenewals); Assert.Equal(1, client.Calls);
        }
        finally { release.TrySetResult(); gate.Release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedSupervisionOutcome.CoordinatorCompleted, result.Outcome);
        Assert.Equal(AssignedExecutionOutcome.Published, result.Coordinator!.Outcome); Assert.False(result.CoordinatorCancelled);
        Assert.Equal(RequirementProcessingJobState.Succeeded, result.Observation!.Snapshot!.JobState);
        Assert.Equal(0, calls.ActiveRenewals); Assert.Equal(1, calls.Renewals);
        await CombinedPublished(new(seed, request, result.Claim!.Receipt!), result.Coordinator, files, copies, client, 1);
    }

    [Fact]
    public async Task Supervisor_PublicationLocksBeforeTick_NoRenewAfterCoordinatorCompletes()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var clock = new ManualSupervisionClock(); var copies = new Copies(); var client = new ConsumingClient();
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); var release = Signal();
        var probe = new SupervisorObservationLockProbe(); var calls = SupervisionProbe(observation: [probe]);
        var task = Supervisor(files, copies, client, clock, calls, publication: [new Saved(async db =>
        { reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID); await release.Task.WaitAsync(Token); })])
            .ExecuteAsync(SupervisionRequest(seed), seed.Actor, Token);
        try
        {
            var pid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); var blocked = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(pid, blocked); await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, pid);
            Assert.Equal(0, calls.Renewals);
            output.WriteLine($"Supervisor publication PID {pid}; observation blocked PID {blocked}; pg_blocking_pids observed.");
        }
        finally { release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedExecutionOutcome.Published, result.Coordinator!.Outcome);
        Assert.Equal(AssignedSupervisionOutcome.CoordinatorCompleted, result.Outcome); Assert.Equal(0, calls.Renewals);
        Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Supervisor_RequestVersusRenew_NoPostStopConfirmation(bool renewFirst)
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var clock = new ManualSupervisionClock(); var copies = new Copies(); var gate = new SupervisorRenewCommitGate(false);
        var reached = Signal(); var release = Signal(); var stopped = Signal();
        var calls = SupervisionProbe(renew: renewFirst ? [gate] : null);
        if (!renewFirst) calls.AfterObservation = async _ =>
        {
            calls.AfterObservation = null;
            Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(seed))).State);
        };
        var client = new ConsumingClient { Action = async (r, ct) =>
        {
            using var registration = ct.Register(() => stopped.TrySetResult()); reached.TrySetResult();
            await release.Task.WaitAsync(Token); return ConsumingClient.Response(r);
        }};
        var task = Supervisor(files, copies, client, clock, calls).ExecuteAsync(SupervisionRequest(seed), seed.Actor, Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30));
            if (renewFirst)
            {
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(seed))).State);
                gate.Release.TrySetResult();
                Assert.Equal(LeaseOperationState.Renewed, (await SupervisionRead(calls.Renewed)).State);
                await SupervisionRead(clock.Scheduled); clock.Advance(TimeSpan.FromSeconds(30));
            }
            else Assert.Equal(LeaseOperationFailure.CancellationRequested, (await SupervisionRead(calls.Renewed)).Failure);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.False(task.IsCompleted); Assert.Equal(1, calls.Renewals);
        }
        finally { gate.Release.TrySetResult(); release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedSupervisionStopReason.CancellationRequested, result.StopReason);
        Assert.Equal(AssignedSupervisionOutcome.Stopped, result.Outcome); Assert.Null(result.Coordinator);
        var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.NotNull(job.CancellationRequestedAtUtc); Assert.Null(job.FinishedAtUtc); Assert.Null(job.PublicationContentHash);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(renewFirst ? 1 : 0, await db.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
        Assert.Equal(RequirementStatus.Processing, (await db.Requirements.SingleAsync(Token)).Status);
        Released(files, copies, client);
    }

    [Fact]
    public async Task Supervisor_IndeterminateWithOutstandingRead_StopsAndDrainsWithoutHarnessDecision()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var clock = new ManualSupervisionClock(); var copies = new BlockingReadCopies();
        using var client = new SupervisorOutstandingClient(); var committed = false;
        var calls = SupervisionProbe(renew: [new CommitHook(() => { committed = true; throw new IOException("lost"); }),
            new OpenHook(() => { if (committed) throw new IOException("observer offline"); })]);
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);
        var core = new CoordinateAssignedExecutionService(new(new AssignedJobStartStore(H.Options())), store,
            new(store, client, NullLogger<ConsumeExecutionDocumentsService>.Instance),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>())),
            new(new JobPublicationStore(H.Options())), H.Service());
        var supervisor = new SuperviseAssignedExecutionService(new(calls), core, new(calls), new(calls), clock);
        var task = supervisor.ExecuteAsync(SupervisionRequest(seed), seed.Actor, Token);
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            clock.Advance(TimeSpan.FromSeconds(30)); await client.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.True(committed); Assert.False(task.IsCompleted); Assert.Equal(0, copies.Disposals);
            Assert.False(client.Reader!.CanRead); Assert.Equal(1, calls.Renewals);
        }
        finally { copies.Release.TrySetResult(); }
        var result = await task; if (client.Read is not null) await client.Read;
        Assert.Equal(AssignedSupervisionOutcome.Stopped, result.Outcome); Assert.Equal(LeaseOperationState.Indeterminate, result.LastRenewal!.State);
        Assert.Equal(2, copies.Disposals); Assert.Equal(1, client.Calls); Assert.Equal(0, files.Deletes);
        Assert.Equal(RequirementProcessingJobState.Running, (await CombinedJob()).State);
        await using var db = fixture.CreateDbContext(); Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Single(await db.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/1.pdf"), Token));
    }

    private sealed class SupervisorOutstandingClient : IVerifiedExecutionDocumentClient, IDisposable
    {
        internal Stream? Reader; internal Task<int>? Read; internal int Calls;
        internal readonly TaskCompletionSource Cancelled = Signal();
        private CancellationTokenRegistration registration;
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest r, CancellationToken ct)
        {
            Calls++; Reader = r.Files[0].Content; registration = ct.Register(() => Cancelled.TrySetResult());
            Read = Reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();
            return Task.FromResult(ConsumingClient.Response(r));
        }
        public void Dispose() => registration.Dispose();
    }

    private sealed class SupervisorRenewCommitGate(bool lost) : DbTransactionInterceptor
    {
        internal readonly TaskCompletionSource Reached = Signal(), Release = Signal(), Cancelled = Signal();
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Reached.TrySetResult(); await Release.Task.WaitAsync(Token);
            if (lost) throw new IOException("late response loss");
        }
    }

    private sealed class SupervisorObservationLockProbe : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pre_quotes") && command.CommandText.Contains("FOR SHARE"))
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
