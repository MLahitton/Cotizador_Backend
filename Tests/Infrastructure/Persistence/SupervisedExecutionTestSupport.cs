using System.Data.Common;
using System.Threading.Channels;
using Application.PreQuotes.ProcessRequirement;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private static ClaimInitialLease SupervisionRequest(Seed s) => new(s.Execution.JobId, s.Execution.RequirementId,
        s.Execution.ExecutionRequestId, Guid.NewGuid(), Guid.NewGuid());

    private SuperviseAssignedExecutionService Supervisor(Files files, Copies copies, ConsumingClient client, ManualSupervisionClock clock,
        SupervisionCalls calls, IInterceptor[]? start = null, IInterceptor[]? publication = null) =>
        new(new(calls), Coordinator(files, copies, client, start: start, publication: publication), new(calls), new(calls), clock);

    private SupervisionCalls SupervisionProbe(IInterceptor[]? claim = null, IInterceptor[]? renew = null, IInterceptor[]? observation = null) =>
        new(new InitialLeaseClaimStore(H.Options(claim ?? [])), new ActiveLeaseRenewalStore(H.Options(renew ?? [])),
            new AssignedExecutionObservationStore(H.Options(observation ?? [])));

    // Transparent probes: all mutations and observations still execute the real stores.
    private sealed class SupervisionCalls(IInitialLeaseClaimStore claims, IActiveLeaseRenewalStore renewals,
        IAssignedExecutionObservationStore observations) : IInitialLeaseClaimStore, IActiveLeaseRenewalStore, IAssignedExecutionObservationStore
    {
        internal int Claims, Renewals, Observations, ActiveRenewals, MaximumActive;
        internal readonly List<RenewActiveLease> Inputs = [];
        internal readonly Channel<LeaseOperationResult> Renewed = Channel.CreateUnbounded<LeaseOperationResult>();
        internal readonly Channel<AssignedExecutionObservationResult> Observed = Channel.CreateUnbounded<AssignedExecutionObservationResult>();
        internal Func<AssignedExecutionObservationResult, Task>? AfterObservation;
        public async Task<LeaseOperationResult> ClaimInitialV2Async(ClaimInitialLease r, Guid actor, CancellationToken ct)
        { Interlocked.Increment(ref Claims); return await claims.ClaimInitialV2Async(r, actor, ct); }
        public async Task<LeaseOperationResult> RenewActiveV2Async(RenewActiveLease r, Guid actor, CancellationToken ct)
        {
            Interlocked.Increment(ref Renewals); Inputs.Add(r);
            var active = Interlocked.Increment(ref ActiveRenewals); MaximumActive = Math.Max(active, MaximumActive);
            try { var result = await renewals.RenewActiveV2Async(r, actor, ct); Renewed.Writer.TryWrite(result); return result; }
            finally { Interlocked.Decrement(ref ActiveRenewals); }
        }
        public async Task<AssignedExecutionObservationResult> ObserveAsync(AssignedExecutionObservationRequest r, Guid actor, CancellationToken ct)
        {
            Interlocked.Increment(ref Observations);
            var result = await observations.ObserveAsync(r, actor, ct);
            if (AfterObservation is not null) await AfterObservation(result);
            Observed.Writer.TryWrite(result); return result;
        }
    }

    private sealed class ManualSupervisionClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ClockTimer> timers = [];
        private long ticks;
        internal readonly Channel<TimeSpan> Scheduled = Channel.CreateUnbounded<TimeSpan>();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) return ticks; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ClockTimer(this, callback, state);
            timer.Change(dueTime, period); return timer;
        }
        internal void Advance(TimeSpan elapsed)
        {
            List<ClockTimer> due;
            lock (gate)
            {
                ticks += elapsed.Ticks;
                due = timers.Where(x => x.Due <= ticks).ToList();
                foreach (var timer in due) { timers.Remove(timer); timer.Due = long.MaxValue; }
            }
            foreach (var timer in due) timer.Callback(timer.State);
        }
        private sealed class ClockTimer(ManualSupervisionClock owner, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal long Due;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                lock (owner.gate)
                {
                    if (disposed) return false;
                    owner.timers.Remove(this);
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.ticks + dueTime.Ticks;
                    owner.timers.Add(this);
                    owner.Scheduled.Writer.TryWrite(dueTime); return true;
                }
            }
            public void Dispose() { lock (owner.gate) { disposed = true; owner.timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static async Task<T> SupervisionRead<T>(Channel<T> channel) =>
        await channel.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);

    private sealed class RenewalAfterWrite(Action action) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("WITH clock", StringComparison.Ordinal))
            {
                await result.DisposeAsync();
                action();
            }
            return result;
        }
    }

    private sealed class SupervisionOpenGate(Func<bool> enabled) : DbConnectionInterceptor
    {
        internal readonly TaskCompletionSource Reached = Signal(), Release = Signal();
        internal CancellationToken ObservedToken;
        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (enabled()) { ObservedToken = cancellationToken; Reached.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    private sealed class SupervisionAfterCommit(Func<Task> action) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => action();
    }

    private sealed class SupervisionClaimGate(TaskCompletionSource<int> reached, TaskCompletionSource release) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("WITH clock", StringComparison.Ordinal))
            {
                reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
