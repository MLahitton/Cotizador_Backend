using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExperienceAuthorizationPostgreSqlTests
{
    private sealed class AdmissionCommitGate : DbTransactionInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
    [Theory]
    [InlineData("same")] [InlineData("different")] [InlineData("admit-revoke")]
    [InlineData("revoke-admit")] [InlineData("revoke-revoke")]
    public async Task Admission_Races_PostgreSqlOrder_OneEffect(string scenario)
    {
        var (s, job) = await AdmissionSetup(); var before = await AdmissionSnapshot([.. DispatchColumns, "authorization_revoked_at_utc"]);
        var candidate = Candidate(job.Id); var second = scenario == "same" ? candidate : Candidate(job.Id);
        var gate = new AdmissionCommitGate(); var winnerStore = AdmissionStore(gate); var loserProbe = new AdmissionProbe(); var loserStore = AdmissionStore(loserProbe);
        var winnerRevokes = scenario.StartsWith("revoke-"); var loserRevokes = scenario.EndsWith("-revoke");
        async Task<object> Invoke(bool revoke, global::Infrastructure.Persistence.Repositories.ExperienceAuthorizationStore store, ExperienceAdmissionCandidate c) =>
            revoke ? await store.RevokeAsync(job.Id, s.Actor, Token) : await store.AdmitAsync(c, Token);
        var winner = Invoke(winnerRevokes, winnerStore, candidate); Task<object>? loser = null;
        try
        {
            var winnerPid = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            loser = Invoke(loserRevokes, loserStore, second);
            var loserPid = await loserProbe.Quote.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winnerPid, loserPid);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loserPid, winnerPid);
            output.WriteLine($"Admission race {scenario}: winner={winnerPid}, blocked={loserPid}, pg_blocking_pids observed");
            gate.Release.TrySetResult();
            var first = await winner; var last = await loser;
            if (winnerRevokes) Assert.Equal(ExperienceRevocationState.Revoked, Assert.IsType<ExperienceRevocationResult>(first).State);
            else Assert.Equal(ExperienceAdmissionState.Admitted, Assert.IsType<ExperienceAdmissionWriteResult>(first).State);
            if (loserRevokes)
            {
                var result = Assert.IsType<ExperienceRevocationResult>(last);
                Assert.Equal(winnerRevokes ? ExperienceRevocationState.AlreadyRevoked : ExperienceRevocationState.Revoked, result.State);
                if (winnerRevokes) Assert.Equal(Assert.IsType<ExperienceRevocationResult>(first).RevokedAtUtc, result.RevokedAtUtc);
                Assert.Equal(winnerRevokes ? 0 : 1, loserProbe.Dml);
            }
            else
            {
                var result = Assert.IsType<ExperienceAdmissionWriteResult>(last);
                Assert.Equal(winnerRevokes ? ExperienceAdmissionState.Rejected : ExperienceAdmissionState.AlreadyAdmitted, result.State);
                Assert.Equal(winnerRevokes ? ExperienceAuthorizationFailure.Revoked : ExperienceAuthorizationFailure.None, result.Failure);
                Assert.Null(result.Confirmed); Assert.Equal(0, loserProbe.Dml);
            }
            Assert.Equal(before, await AdmissionSnapshot([.. DispatchColumns, "authorization_revoked_at_utc"]));
            await using var read = fixture.CreateDbContext(); var persisted = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
            if (!winnerRevokes)
            { Assert.Equal(candidate.InstanceId, persisted.DispatchInstanceId); Assert.Equal(candidate.ClaimOperationId, persisted.DispatchClaimOperationId); }
            else Assert.Null(persisted.DispatchAdmittedAtUtc);
        }
        finally { gate.Release.TrySetResult(); await winner; if (loser is not null) await loser; }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Admission_OwnerTransferBothOrders_NoRetroactiveRevocation(bool admissionFirst)
    {
        var (s, job) = await AdmissionSetup();
        var next = User.CreateFromGoogle("transfer@example.test", "Next", null, null, DateTimeOffset.UtcNow);
        Guid project;
        await using (var db = fixture.CreateDbContext())
        { db.Add(next); await db.SaveChangesAsync(Token); project = await db.Projects.Select(x => x.Id).SingleAsync(Token); }
        var allowed = new Dictionary<string, string[]> { ["requirement_processing_jobs"] = DispatchColumns, ["projects"] = ["created_by_user_id"] };
        var before = await AdmissionSnapshotCore(allowed);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var tx = await connection.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand("UPDATE core.projects SET created_by_user_id=@owner WHERE id=@project", connection, tx);
        command.Parameters.AddWithValue("owner", next.Id); command.Parameters.AddWithValue("project", project);
        var gate = new AdmissionCommitGate(); var probe = new AdmissionProbe(); var committed = false;
        Task<ExperienceAdmissionWriteResult>? admission = null; Task<int>? transfer = null;
        try
        {
            if (admissionFirst)
            {
                admission = AdmissionStore(gate).AdmitAsync(Candidate(job.Id), Token);
                var pid = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                transfer = command.ExecuteNonQueryAsync(Token);
                Assert.NotEqual(pid, connection.ProcessID);
                await new JobProtocolTestSupport(fixture).WaitBlocked(connection.ProcessID, pid);
                output.WriteLine($"Admission first={pid}, transfer blocked={connection.ProcessID}");
                gate.Release.TrySetResult();
                Assert.Equal(ExperienceAdmissionState.Admitted, (await admission).State); Assert.Equal(1, await transfer);
                await tx.CommitAsync(Token); committed = true;
            }
            else
            {
                await command.ExecuteNonQueryAsync(Token);
                admission = AdmissionStore(probe).AdmitAsync(Candidate(job.Id), Token);
                var pid = await probe.Project.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                Assert.NotEqual(pid, connection.ProcessID);
                await new JobProtocolTestSupport(fixture).WaitBlocked(pid, connection.ProcessID);
                output.WriteLine($"Transfer first={connection.ProcessID}, admission blocked={pid}");
                await tx.CommitAsync(Token); committed = true;
                var result = await admission;
                Assert.Equal(ExperienceAdmissionState.Rejected, result.State); Assert.Equal(ExperienceAuthorizationFailure.NotFound, result.Failure);
                Assert.Null(result.Confirmed); Assert.Equal(0, probe.Dml);
            }
            await using var read = fixture.CreateDbContext();
            Assert.Equal(next.Id, await read.Projects.Select(x => x.CreatedByUserId).SingleAsync(Token));
            var persisted = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(s.Actor, persisted.AuthorizedActorUserId);
            Assert.Equal(admissionFirst, persisted.DispatchAdmittedAtUtc.HasValue);
            Assert.Null(persisted.AuthorizationRevokedAtUtc); Assert.Null(persisted.ProcessingAttemptId);
            if (admissionFirst)
            {
                var confirmed = (await admission!).Confirmed!;
                Assert.Equal(confirmed.InstanceId, persisted.DispatchInstanceId);
                Assert.Equal(confirmed.ClaimOperationId, persisted.DispatchClaimOperationId);
                Assert.Equal(confirmed.AdmittedAtUtc, persisted.DispatchAdmittedAtUtc);
            }
            Assert.Equal(before, await AdmissionSnapshotCore(allowed));
            Assert.Equal(ExperienceRevocationState.Revoked, (await AdmissionStore().RevokeAsync(job.Id, next.Id, Token)).State);
        }
        finally
        {
            gate.Release.TrySetResult(); if (!committed) await tx.RollbackAsync(Token);
            if (admission is not null) await admission; if (transfer is not null) await transfer;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Admission_Waits_RealExpiryOrCaller_NoPermit(bool cancel)
    {
        var (_, job) = await AdmissionSetup(cancel ? 86400 : 3);
        var h = new JobTerminationTestSupport(fixture);
        Assert.True(await h.Now() < job.AuthorizationExpiresAtUtc);
        var before = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var tx = await connection.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand("SELECT id FROM core.pre_quotes FOR UPDATE", connection, tx);
        await command.ExecuteNonQueryAsync(Token);
        var pending = new ExperienceAdmissionService(AdmissionStore(probe)).AdmitAsync(job.Id, caller.Token);
        try
        {
            var pid = await probe.Quote.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await new JobProtocolTestSupport(fixture).WaitBlocked(pid, connection.ProcessID);
            if (cancel) caller.Cancel();
            else
            {
                DateTimeOffset now;
                do { now = await h.Now(); if (now < job.AuthorizationExpiresAtUtc) await Task.Delay(20, Token); } while (now < job.AuthorizationExpiresAtUtc);
                output.WriteLine($"DB {now:O} >= expiry {job.AuthorizationExpiresAtUtc:O} before release, blocked={pid}, holder={connection.ProcessID}");
            }
            await tx.RollbackAsync(Token);
            if (cancel)
            { var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Equal(caller.Token, ex.CancellationToken); }
            else
            { var result = await pending; Assert.Equal(ExperienceAuthorizationFailure.Expired, result.Failure); Assert.Null(result.Permit); }
            Assert.Equal(0, probe.Dml); Assert.Equal(before, await AdmissionSnapshot());
        }
        finally
        {
            await tx.DisposeAsync();
            try { await pending; } catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
        }
    }

    private sealed class AdmissionAfterWrite(bool unknown, CancellationTokenSource? caller) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("WITH clock")) return result;
            await result.DisposeAsync();
            if (unknown) await command.Connection!.CloseAsync();
            caller?.Cancel();
            throw new IOException("After real write, before commit");
        }
    }
    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)]
    [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, false, true)]
    public async Task Admission_PostWriteRollback_KnownVersusUnknown(bool revoke, bool unknown, bool cancel)
    {
        var (s, job) = await AdmissionSetup(); var before = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var store = AdmissionStore(probe, new AdmissionAfterWrite(unknown, cancel ? caller : null));
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            { if (revoke) await store.RevokeAsync(job.Id, s.Actor, caller.Token); else await new ExperienceAdmissionService(store).AdmitAsync(job.Id, caller.Token); });
            Assert.Equal(caller.Token, error.CancellationToken);
        }
        else if (revoke)
        {
            var result = await store.RevokeAsync(job.Id, s.Actor, Token);
            Assert.Equal(unknown ? ExperienceRevocationState.Indeterminate : ExperienceRevocationState.PersistenceError, result.State);
            Assert.Null(result.RevokedAtUtc);
        }
        else
        {
            var result = await new ExperienceAdmissionService(store).AdmitAsync(job.Id, Token);
            Assert.Equal(unknown ? ExperienceAdmissionState.Indeterminate : ExperienceAdmissionState.PersistenceError, result.State);
            Assert.Null(result.Permit);
        }
        Assert.Equal(1, probe.Dml); Assert.Equal(before, await AdmissionSnapshot());
    }
}
