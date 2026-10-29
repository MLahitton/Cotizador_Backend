using System.Data.Common;
using Application.PublicExperience;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicPreparationVerifiedPostgreSqlTests
{
    [Fact]
    public async Task PausedStorage_HoldsNoSubmissionLock()
    {
        using var data = await Seed();
        var reached = Signal(); var release = Signal();
        data.BeforeOpen = async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); };
        await using var caller = fixture.CreateDbContext();
        var pending = Service(caller, data).ExecuteAsync("public-test-id", Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            await using var probe = new NpgsqlConnection(fixture.ConnectionString);
            await probe.OpenAsync(Token);
            await using (var tx = await probe.BeginTransactionAsync(Token))
            {
                await using var command = new NpgsqlCommand("SELECT id FROM core.public_experience_submissions WHERE submission_id='public-test-id' FOR UPDATE NOWAIT", probe, tx);
                Assert.IsType<Guid>(await command.ExecuteScalarAsync(Token));
                output.WriteLine($"Storage paused; Submission NOWAIT acquired by PID {probe.ProcessID}");
            }
            release.TrySetResult();
            Assert.True((await pending)!.IsSuccess);
        }
        finally { release.TrySetResult(); await pending; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompetingPreparers_ObservedFinalLocks_AndLateFailureCannotDestroySuccess(bool firstFails, bool secondFails)
    {
        using var data = await Seed();
        var other = new Files(data.Root, output);
        var aCopied = Signal(); var bCopied = Signal(); var aContinue = Signal(); var bContinue = Signal();
        var releaseCommit = Signal();
        var blocker = new CommitGate(releaseCommit.Task);
        var probe = new SubmissionProbe();
        data.AfterSave = async () => { aCopied.TrySetResult(); await aContinue.Task.WaitAsync(Token); if (firstFails) throw new IOException("Late first copy failure"); };
        other.AfterSave = async () => { bCopied.TrySetResult(); await bContinue.Task.WaitAsync(Token); probe.Enabled = true; if (secondFails) throw new IOException("Late second copy failure"); };
        await using var aDb = Context(blocker);
        await using var bDb = Context(probe);
        var a = Service(aDb, data).ExecuteAsync("public-test-id", Token);
        var b = Service(bDb, other).ExecuteAsync("public-test-id", Token);
        try
        {
            await Task.WhenAll(aCopied.Task, bCopied.Task).WaitAsync(TimeSpan.FromSeconds(20), Token);
            aContinue.TrySetResult();
            var winner = await blocker.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            bContinue.TrySetResult();
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"firstFails={firstFails},secondFails={secondFails}; winner={winner}; blocked={loser}; Lock and named blocker observed");
            releaseCommit.TrySetResult();
            var first = await a; var second = await b;
            Assert.Equal(!firstFails, first!.IsSuccess);
            Assert.True(second!.IsSuccess, second.ErrorCode);
            Assert.Equal(!firstFails, second.IsReplay);
            if (!firstFails) Assert.Equal(first.RequirementId, second.RequirementId);
            await using var read = fixture.CreateDbContext();
            Assert.Single(await read.PreQuotes.ToListAsync(Token));
            Assert.Single(await read.Requirements.ToListAsync(Token));
            var file = Assert.Single(await read.RequirementFiles.ToListAsync(Token));
            Assert.Equal(Hash, file.StoredSha256);
            var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
            Assert.Null(submission.RequirementPreparationErrorCode); Assert.NotNull(submission.RequirementPreparedAtUtc);
            Assert.Equal(second.RequirementId, submission.RequirementId);
            Assert.Equal(3, Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories).Length);
            Assert.Equal(0, data.Deletes + other.Deletes);
            var snapshot = await Graph();
            Assert.True((await Service(aDb, data).ExecuteAsync("public-test-id", Token))!.IsReplay);
            Assert.Equal(snapshot, await Graph());
        }
        finally
        {
            aContinue.TrySetResult(); bContinue.TrySetResult(); releaseCommit.TrySetResult();
            await Task.WhenAll(a, b);
        }
    }

    [Fact]
    public async Task SaveResponseFailure_RollsBackWholeGraph_ThenRecordsError()
    {
        using var data = await Seed();
        var fault = new SavedFailure();
        await using var caller = Context(fault);
        var result = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.True(fault.Injected); Assert.False(result!.IsSuccess);
        Assert.Equal(PublicExperienceRequirementPreparationErrors.Failed, result.ErrorCode);
        await using var read = fixture.CreateDbContext();
        Assert.False(await read.Requirements.AnyAsync(Token));
        Assert.False(await read.PreQuotes.AnyAsync(Token));
        Assert.False(await read.RequirementFiles.AnyAsync(Token));
        Assert.Equal(2, Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(0, data.Deletes);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("after-normalized")]
    [InlineData("unavailable")]
    [InlineData("blocked")]
    public async Task CommitUncertainty_ReadOnlyBoundedObservation_NoCompensation(string boundary)
    {
        using var data = await Seed();
        if (boundary == "after-normalized")
        {
            await using var edit = fixture.CreateDbContext();
            // Historical metadata accepted by the existing Requirement factory.
            await edit.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submission_files SET original_file_name=' first.pdf ',content_type='APPLICATION/PDF',stored_length_bytes=NULL,stored_evidence_at_utc=NULL,stored_evidence_version=NULL,content_hash=lower(content_hash)", Token);
        }
        var probe = new SubmissionProbe();
        var writes = new WriteCounter();
        NpgsqlConnection? external = null;
        NpgsqlTransaction? held = null;
        string? committed = null;
        var fault = new CommitFailure(boundary == "before", async () =>
        {
            committed = await Graph();
            if (boundary == "blocked")
            {
                external = new NpgsqlConnection(fixture.ConnectionString);
                await external.OpenAsync(Token);
                held = await external.BeginTransactionAsync(Token);
                await using var command = new NpgsqlCommand("SELECT id FROM core.public_experience_submissions FOR UPDATE", external, held);
                await command.ExecuteScalarAsync(Token);
            }
            writes.Enabled = true;
            probe.Enabled = true;
            probe.Unavailable = boundary == "unavailable";
        });
        await using var caller = Context(fault, probe, writes);
        var pending = Service(caller, data).ExecuteAsync("public-test-id", Token);
        try
        {
            if (boundary == "blocked")
            {
                var observer = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                Assert.NotEqual(external!.ProcessID, observer);
                await new JobProtocolTestSupport(fixture).WaitBlocked(observer, external.ProcessID);
                await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                Assert.True(probe.ObservationToken.IsCancellationRequested);
                output.WriteLine($"Reconciliation: observer={observer}; blocker={external.ProcessID}; server Lock observed, production token expired");
            }
            if (boundary.StartsWith("after", StringComparison.Ordinal)) Assert.True((await pending)!.IsSuccess);
            else await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(45), Token));
            Assert.True(fault.Injected);
            Assert.Equal(0, writes.Count);
            if (held is not null) { await held.DisposeAsync(); held = null; }
            if (committed is not null) Assert.Equal(committed, await Graph());
            await using var read = fixture.CreateDbContext();
            Assert.Equal(boundary == "before" ? 0 : 1, await read.Requirements.CountAsync(Token));
            Assert.Equal(2, Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories).Length);
            Assert.Equal(0, data.Deletes);
        }
        finally
        {
            if (held is not null) await held.DisposeAsync();
            if (external is not null) await external.DisposeAsync();
            try { await pending; } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CallerCancellationDuringObservation_PreservesCommitAndReplay(bool cancelCaller, bool failObservation)
    {
        using var data = await Seed();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var release = Signal();
        var observer = new ObservationGate(release.Task, failObservation);
        var writes = new WriteCounter();
        string? committed = null;
        var fault = new CommitFailure(false, async () =>
        {
            committed = await Graph();
            writes.Enabled = true;
            observer.Enabled = true;
        });
        await using var caller = Context(fault, observer, writes);
        var pending = Service(caller, data).ExecuteAsync("public-test-id", cancel.Token);
        try
        {
            await observer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.True(fault.Injected);
            Assert.NotEqual(fault.CommittedContextId, observer.ContextId);
            Assert.NotEqual(cancel.Token, observer.ObservationToken);
            Assert.False(pending.IsCompleted);
            if (cancelCaller) cancel.Cancel();
            Assert.False(observer.ObservationToken.IsCancellationRequested);
            release.TrySetResult();
            var exception = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(20), Token));
            Assert.Equal(failObservation, observer.Failed);
            Assert.Equal(!failObservation, observer.ReadExecuted);
            Assert.Equal(0, writes.Count);
            Assert.NotNull(committed);
            Assert.Equal(committed, await Graph());
            await using var read = fixture.CreateDbContext();
            var quote = Assert.Single(await read.PreQuotes.ToListAsync(Token));
            var requirement = Assert.Single(await read.Requirements.ToListAsync(Token));
            var file = Assert.Single(await read.RequirementFiles.ToListAsync(Token));
            var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
            Assert.Equal(quote.Id, submission.PreQuoteId);
            Assert.Equal(requirement.Id, submission.RequirementId);
            Assert.Equal(quote.Id, requirement.PreQuoteId);
            Assert.NotNull(submission.RequirementPreparedAtUtc);
            Assert.Null(submission.RequirementPreparationErrorCode);
            Assert.Equal(Hash, file.StoredSha256);
            Assert.Equal(3, file.StoredLengthBytes);
            Assert.Equal((short)1, file.StoredEvidenceVersion);
            Assert.NotNull(file.StoredEvidenceAtUtc);
            Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(data.Root, file.StorageKey), Token));
            Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
            Assert.Equal(1, data.Opens); Assert.Equal(1, data.Saves); Assert.Equal(0, data.Deletes);
            observer.Enabled = false;
            using var replayToken = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var replay = await Service(caller, data).ExecuteAsync("public-test-id", replayToken.Token);
            Assert.True(replay!.IsSuccess); Assert.True(replay.IsReplay);
            Assert.Equal(submission.RequirementId, replay.RequirementId);
            Assert.Equal(submission.PreQuoteId, replay.PreQuoteId);
            Assert.Equal(committed, await Graph());
            Assert.Equal(1, data.Opens); Assert.Equal(1, data.Saves); Assert.Equal(0, data.Deletes);
            Assert.Equal(0, writes.Count);
            output.WriteLine($"cancelCaller={cancelCaller}; failObservation={failObservation}; committedContext={fault.CommittedContextId}; observerContext={observer.ContextId}; callerOutcome={exception?.GetType().Name ?? "Success"}; commit/bytes/replay unchanged");
            if (cancelCaller)
                Assert.Equal(cancel.Token, Assert.IsType<OperationCanceledException>(exception).CancellationToken);
            else
            {
                Assert.Null(exception);
                Assert.True((await pending)!.IsSuccess);
                Assert.False((await pending)!.IsReplay);
            }
        }
        finally
        {
            release.TrySetResult();
            try { await pending; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    private sealed class ObservationGate(Task release, bool fail) : DbCommandInterceptor
    {
        internal bool Enabled, Failed, ReadExecuted;
        internal Guid ContextId;
        internal CancellationToken ObservationToken;
        internal TaskCompletionSource Reached { get; } = Signal();
        private bool Matches(DbCommand command) => Enabled
            && command.CommandText.Contains("core.public_experience_submissions", StringComparison.Ordinal);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Matches(command))
            {
                ContextId = eventData.Context!.ContextId.InstanceId;
                ObservationToken = cancellationToken;
                Reached.TrySetResult();
                await release.WaitAsync(cancellationToken);
                if (fail) { Failed = true; throw new IOException("Observation failed after barrier"); }
            }
            return result;
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Matches(command)) ReadExecuted = true;
            return ValueTask.FromResult(result);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CommitGate(Task release) : DbTransactionInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await release.WaitAsync(cancellationToken); return result;
        }
    }

    private sealed class SubmissionProbe : DbCommandInterceptor
    {
        internal bool Enabled, Unavailable;
        internal CancellationToken ObservationToken;
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = Signal();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("core.public_experience_submissions", StringComparison.Ordinal))
            {
                ObservationToken = cancellationToken;
                cancellationToken.Register(() => Cancelled.TrySetResult());
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                if (Unavailable) throw new IOException("Observation unavailable");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitFailure(bool before, Func<Task> committed) : DbTransactionInterceptor
    {
        internal bool Injected;
        internal Guid CommittedContextId;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (before && !Injected) { Injected = true; throw new IOException("Commit not sent"); }
            return ValueTask.FromResult(result);
        }
        public override async Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!before && !Injected) { Injected = true; CommittedContextId = eventData.Context!.ContextId.InstanceId; await committed(); throw new IOException("Lost commit response"); }
        }
    }

    private sealed class SavedFailure : SaveChangesInterceptor
    {
        internal bool Injected;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Injected) { Injected = true; throw new IOException("Lost save response before commit"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class WriteCounter : DbCommandInterceptor
    {
        internal bool Enabled;
        internal int Count;
        private void Inspect(DbCommand command)
        {
            if (Enabled && new[] { "INSERT", "UPDATE core.", "DELETE" }.Any(s => command.CommandText.Contains(s, StringComparison.Ordinal))) Count++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
    }
}
