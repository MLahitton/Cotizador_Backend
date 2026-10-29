using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Fact]
    public async Task ConcurrentPreparedCopies_LoserDisposesItsCopiesWithoutPackage()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        var a = new Copies(); var b = new Copies();
        var aReady = Signal(); var bReady = Signal(); var aRelease = Signal(); var bRelease = Signal();
        a.AfterCopy = async () => { if (a.Created.Count == 2) { aReady.TrySetResult(); await aRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); } };
        b.AfterCopy = async () => { if (b.Created.Count == 2) { bReady.TrySetResult(); await bRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); } };
        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var hook = new CommandHook(async command =>
        {
            held.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        });
        var probe = new JobProtocolTestSupport.QuoteProbe(() => b.Created.Count == 2);
        var first = Store(files, a, hook).PrepareAsync(seed.Execution, seed.Actor, Token);
        Task<ExecutionDocumentPreparationResult>? second = null;
        try
        {
            await aReady.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            second = Store(files, b, probe).PrepareAsync(seed.Execution, seed.Actor, Token);
            await bReady.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            aRelease.TrySetResult();
            var holderPid = await held.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            bRelease.TrySetResult();
            var blockedPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(holderPid, blockedPid);
            await new JobProtocolTestSupport(fixture).WaitBlocked(blockedPid, holderPid);
            output.WriteLine($"Both prepared: holder={holderPid}, blocked={blockedPid}, pg_blocking_pids confirmed");
            release.TrySetResult();
            var winner = await first; var loser = await second;
            Assert.Equal(ExecutionDocumentPreparationState.Registered, winner.State);
            Assert.Equal(ExecutionDocumentPreparationState.AlreadyRegistered, loser.State);
            Assert.Equal(winner.Evidence!.Id, loser.Evidence!.Id);
            Assert.Null(loser.Package);
            Assert.All(b.Created, c => Assert.True(c.Disposed));
            Assert.All(a.Created, c => Assert.False(c.Disposed));
            await winner.Package!.DisposeAsync();
            Assert.Equal(4, files.Opens); Assert.Equal(0, files.Deletes);
            await AssertRows(1);
        }
        finally
        {
            aRelease.TrySetResult(); bRelease.TrySetResult(); release.TrySetResult();
            var r = await first; if (r.Package is not null) await r.Package.DisposeAsync();
            if (second is not null) { r = await second; if (r.Package is not null) await r.Package.DisposeAsync(); }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForeignActor_IncludingAdmin_CannotReadOrRecognize(bool admin)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        await using var db = fixture.CreateDbContext();
        var other = User.CreateFromGoogle("other@example.test", "Other", null, null, await H.Now());
        if (admin) other.ChangeRole(UserRole.Admin, await H.Now());
        db.Add(other); await db.SaveChangesAsync(Token);
        var copies = new Copies();
        var result = await Store(files, copies).PrepareAsync(seed.Execution, other.Id, Token);
        Assert.Equal(ExecutionDocumentPreparationFailure.NotFound, result.Failure);
        Assert.Null(result.Evidence); Assert.Null(result.Package); Assert.Equal(0, files.Opens);
        var registered = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.Registered, registered.State);
        await registered.Package!.DisposeAsync();
        var deniedReplay = await Store(files, copies).PrepareAsync(seed.Execution, other.Id, Token);
        Assert.Equal(ExecutionDocumentPreparationFailure.NotFound, deniedReplay.Failure);
        Assert.Null(deniedReplay.Evidence); Assert.Equal(2, files.Opens);
    }

    [Theory]
    [InlineData("user")] [InlineData("client")] [InlineData("project")] [InlineData("owner")]
    [InlineData("links")] [InlineData("cancel")] [InlineData("state")] [InlineData("metadata")]
    public async Task ChangedContextBetweenCopyAndRegistration_RejectsWithoutWrites(string change)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, true);
        var copies = new Copies();
        var injected = false;
        var mutationCompleted = false;
        copies.AfterCopy = async () =>
        {
            if (injected) return;
            injected = true;
            await using var db = fixture.CreateDbContext();
            switch (change)
            {
                case "user": await db.Database.ExecuteSqlRawAsync("UPDATE identity.users SET is_active=false", Token); break;
                case "client": await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token); break;
                case "project": await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false", Token); break;
                case "owner":
                    var other = User.CreateFromGoogle("new-owner@example.test", "New", null, null, await H.Now());
                    db.Add(other); await db.SaveChangesAsync(Token);
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={other.Id}", Token); break;
                case "links": await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submissions SET requirement_id=NULL", Token); break;
                case "cancel": await H.Call("request", seed); break;
                case "state": await H.Call("fail", seed); break;
                case "metadata": await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET original_file_name='changed.pdf'", Token); break;
            }
            mutationCompleted = true;
        };
        var writes = new JobProtocolTestSupport.WriteProbe();
        var result = await Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.True(injected); Assert.True(mutationCompleted);
        Assert.Equal(ExecutionDocumentPreparationState.Rejected, result.State);
        Assert.NotEqual(ExecutionDocumentPreparationFailure.SourceUnavailable, result.Failure);
        Assert.Null(result.Package); await AssertRows(0);
        Assert.Equal(0, writes.Count); Assert.Equal(0, files.Deletes);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task DuringCopy_AllProtocolRowsCanBeLockedByAnotherConnection()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, true);
        var copies = new Copies();
        var checkedLocks = false;
        copies.AfterCopy = async () =>
        {
            if (checkedLocks) return;
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using var transaction = await connection.BeginTransactionAsync(Token);
            foreach (var table in new[] { "core.public_experience_submissions", "identity.users", "core.clients", "core.projects",
                "core.pre_quotes", "core.requirements", "core.requirement_processing_jobs", "core.requirement_processing_attempts", "core.requirement_files" })
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"SELECT * FROM {table} FOR UPDATE NOWAIT";
                await using var reader = await command.ExecuteReaderAsync(Token);
                Assert.True(await reader.ReadAsync(Token));
            }
            checkedLocks = true;
            output.WriteLine($"All protocol locks available during copy, PID {connection.ProcessID}");
        };
        var result = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.True(checkedLocks); Assert.Equal(ExecutionDocumentPreparationState.Registered, result.State);
        await result.Package!.DisposeAsync();
    }

    [Theory]
    [InlineData("register")] [InlineData("cancel-first")] [InlineData("cancel-second")]
    public async Task ConcurrentRegistrationAndCancellation_UsesObservedServerLocks(string mode)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        var firstCopies = new Copies(); var secondCopies = new Copies();
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var holder = new CommandHook(async command =>
        {
            reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        });
        var probe = new JobProtocolTestSupport.QuoteProbe();
        Task<ExecutionDocumentPreparationResult>? first = null, second = null;
        Task<JobTerminationResult>? cancellation = null;
        try
        {
            if (mode == "cancel-first") cancellation = H.Call("request", seed, H.Service(holder));
            else first = Store(files, firstCopies, holder).PrepareAsync(seed.Execution, seed.Actor, Token);
            var holderPid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            if (mode == "cancel-second") cancellation = H.Call("request", seed, H.Service(probe));
            else second = Store(files, secondCopies, probe).PrepareAsync(seed.Execution, seed.Actor, Token);
            var blockedPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(holderPid, blockedPid);
            await new JobProtocolTestSupport(fixture).WaitBlocked(blockedPid, holderPid);
            output.WriteLine($"{mode}: holder={holderPid}, blocked={blockedPid}, pg_blocking_pids confirmed");
            release.TrySetResult();
            if (first is not null)
            {
                var registered = await first;
                Assert.Equal(ExecutionDocumentPreparationState.Registered, registered.State);
                await registered.Package!.DisposeAsync();
            }
            if (second is not null)
            {
                var result = await second;
                Assert.Equal(mode == "cancel-first" ? ExecutionDocumentPreparationState.Rejected : ExecutionDocumentPreparationState.AlreadyRegistered, result.State);
                Assert.Null(result.Package);
                if (mode == "cancel-first") Assert.Equal(ExecutionDocumentPreparationFailure.CancellationRequested, result.Failure);
            }
            if (cancellation is not null) Assert.Equal(JobTerminationState.CancellationRequested, (await cancellation).State);
            await AssertRows(mode == "cancel-first" ? 0 : 1);
            Assert.All(firstCopies.Created.Concat(secondCopies.Created), c => Assert.True(c.Disposed));
            Assert.Equal(0, files.Deletes);
        }
        finally
        {
            release.TrySetResult();
            if (first is not null) { var r = await first; if (r.Package is not null) await r.Package.DisposeAsync(); }
            if (second is not null) { var r = await second; if (r.Package is not null) await r.Package.DisposeAsync(); }
            if (cancellation is not null) await cancellation;
        }
    }

    [Fact]
    public async Task BlockedObservation_ExpiresIndependentBudget_WithoutCompensation()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files); var copies = new Copies();
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(Token);
        NpgsqlTransaction? transaction = null;
        var observing = false;
        var probe = new JobProtocolTestSupport.QuoteProbe(() => observing);
        var writes = new JobProtocolTestSupport.WriteProbe(() => observing);
        var hook = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            transaction = await blocker.BeginTransactionAsync(Token);
            await using var command = blocker.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT id FROM core.pre_quotes WHERE id=@id FOR UPDATE";
            command.Parameters.AddWithValue("id", seed.Quote);
            await command.ExecuteScalarAsync(Token);
            observing = true; throw new IOException("Lost real commit response");
        });
        var pending = Store(files, copies, hook, probe, writes).PrepareAsync(seed.Execution, seed.Actor, Token);
        try
        {
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(pid, blocker.ProcessID);
            await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocker.ProcessID);
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(45), Token);
            Assert.True(probe.Cancelled.Task.IsCompletedSuccessfully);
            Assert.Equal(ExecutionDocumentPreparationState.Indeterminate, result.State);
            Assert.Null(result.Package); await AssertRows(1);
            Assert.Equal(0, writes.Count); Assert.Equal(0, files.Deletes);
            Assert.All(copies.Created, c => Assert.True(c.Disposed));
            output.WriteLine($"Observation PID={pid}, blocker={blocker.ProcessID}; Lock observed, production budget cancelled");
        }
        finally
        {
            if (transaction is not null) { await transaction.RollbackAsync(CancellationToken.None); await transaction.DisposeAsync(); }
            var result = await pending; if (result.Package is not null) await result.Package.DisposeAsync();
        }
    }

    [Fact]
    public async Task CorruptedStoredManifest_IsNotTrusted_DespiteSameHash()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files); var copies = new Copies();
        var first = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.Registered, first.State);
        await first.Package!.DisposeAsync();
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_documents SET original_file_name='forged.pdf'", Token);
        var writes = new JobProtocolTestSupport.WriteProbe();
        var replay = await Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.Conflict, replay.State);
        Assert.Null(replay.Package); Assert.Equal(2, files.Opens); Assert.Equal(0, writes.Count);
    }
}
