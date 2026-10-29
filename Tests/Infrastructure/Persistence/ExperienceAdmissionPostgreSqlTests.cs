using System.Data.Common;
using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExperienceAuthorizationPostgreSqlTests
{
    private async Task<(Seed Seed, RequirementProcessingJob Job)> AdmissionSetup(double expirySeconds = 86400)
    {
        var s = await Create();
        var now = await new JobTerminationTestSupport(fixture).Now();
        var at = now.AddSeconds(expirySeconds - 86400);
        var job = RequirementProcessingJob.CreateForAutomaticExperience(Guid.NewGuid(), s.Requirement, s.Submission,
            Guid.NewGuid(), Guid.NewGuid(), s.Actor, 1, at, at.AddHours(24));
        await using var db = fixture.CreateDbContext();
        var hash = new string('A', 64);
        // Replace this fixture's legacy source with complete stored evidence before measuring I.2.
        await db.Set<PublicExperienceSubmissionFile>().Where(x => x.SubmissionId == s.Submission).ExecuteDeleteAsync(Token);
        db.Add(PublicExperienceSubmissionFile.CreateWithStoredEvidence(Guid.NewGuid(), s.Submission, 0,
            "input.pdf", "application/pdf", 3, "source/input", hash, at, 3, at, 1));
        db.Add(RequirementFile.CreateWithStoredEvidence(s.Requirement, "input.pdf", "application/pdf", 3,
            "prepared/input", at, hash, 3, at, 1));
        db.Add(job); await db.SaveChangesAsync(Token);
        return (s, job);
    }
    private ExperienceAuthorizationStore AdmissionStore(params IInterceptor[] interceptors) =>
        new(new JobTerminationTestSupport(fixture).Options(interceptors));
    private static ExperienceAdmissionCandidate Candidate(Guid job) => new(job, Guid.NewGuid(), Guid.NewGuid());

    // A fresh connection reads every business table, including unrelated rows and authority.
    private Task<string> AdmissionSnapshot(params string[] omittedJobColumns) =>
        AdmissionSnapshotCore(new Dictionary<string, string[]> { ["requirement_processing_jobs"] = omittedJobColumns });
    private async Task<string> AdmissionSnapshotCore(IReadOnlyDictionary<string, string[]> omittedColumns)
    {
        await using var c = new NpgsqlConnection(fixture.ConnectionString); await c.OpenAsync(Token);
        var tables = new List<(string Schema, string Name)>();
        await using (var cmd = new NpgsqlCommand("SELECT table_schema,table_name FROM information_schema.tables WHERE table_schema IN ('core','identity') AND table_type='BASE TABLE' ORDER BY table_schema,table_name", c))
        await using (var reader = await cmd.ExecuteReaderAsync(Token))
            while (await reader.ReadAsync(Token)) tables.Add((reader.GetString(0), reader.GetString(1)));
        var values = new List<string>();
        foreach (var (schema, name) in tables)
        {
            var expression = "to_jsonb(t)";
            if (omittedColumns.TryGetValue(name, out var columns))
                foreach (var column in columns) expression += " - '" + column.Replace("'", "''") + "'";
            using var builder = new NpgsqlCommandBuilder();
            await using var cmd = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(v ORDER BY v::text),'[]'::jsonb)::text FROM (SELECT {expression} AS v FROM {builder.QuoteIdentifier(schema)}.{builder.QuoteIdentifier(name)} t) x", c);
            values.Add(name + ":" + (string)(await cmd.ExecuteScalarAsync(Token))!);
        }
        return string.Join("\n", values);
    }
    private static readonly string[] DispatchColumns = ["dispatch_admitted_at_utc", "dispatch_instance_id", "dispatch_claim_operation_id"];
    private sealed class AdmissionConnectionProbe : DbConnectionInterceptor
    {
        internal int Opened;
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Opened); return Task.CompletedTask; }
    }
    private sealed class AdmissionProbe : DbCommandInterceptor
    {
        internal int Dml;
        internal int Connections;
        internal TaskCompletionSource<int> Quote { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<int> Project { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText.TrimStart();
            if (sql.StartsWith("WITH clock") || sql.StartsWith("UPDATE ") || sql.StartsWith("INSERT ") || sql.StartsWith("DELETE ")) Interlocked.Increment(ref Dml);
            if (sql.Contains("FOR UPDATE") && sql.Contains("pre_quotes")) Quote.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            if (sql.Contains("FOR SHARE") && sql.Contains("core.projects")) Project.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            Interlocked.Increment(ref Connections);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Admission_ExactEffects_OnePermit_RepeatsNeverReconstruct()
    {
        var (_, job) = await AdmissionSetup(); var before = await AdmissionSnapshot(DispatchColumns);
        var probe = new AdmissionProbe(); var connections = new AdmissionConnectionProbe(); var store = AdmissionStore(probe, connections);
        var result = await new ExperienceAdmissionService(store).AdmitAsync(job.Id, Token);
        Assert.Equal(ExperienceAdmissionState.Admitted, result.State); Assert.NotNull(result.Permit);
        Assert.Empty(typeof(ExperienceExecutionPermit).GetConstructors());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(result.Permit));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ExperienceExecutionPermit>("{}"));
        var p = result.Permit.Admission;
        Assert.Equal(job.Id, p.JobId); Assert.Equal(job.RequirementId, p.RequirementId);
        Assert.Equal(job.ExecutionRequestId, p.ExecutionRequestId); Assert.Equal(job.CorrelationId, p.CorrelationId);
        Assert.Equal(job.SubmissionId, p.SubmissionId); Assert.Equal(job.AuthorizedActorUserId, p.ActorUserId);
        Assert.Equal(job.AuthorizedAtUtc, p.AuthorizedAtUtc); Assert.Equal(job.AuthorizationExpiresAtUtc, p.ExpiresAtUtc);
        Assert.InRange(p.AdmittedAtUtc, p.AuthorizedAtUtc, p.ExpiresAtUtc.AddTicks(-10));
        Assert.Equal(1, probe.Dml); Assert.Equal(before, await AdmissionSnapshot(DispatchColumns));
        await using var read = fixture.CreateDbContext();
        var persisted = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(p.InstanceId, persisted.DispatchInstanceId); Assert.Equal(p.ClaimOperationId, persisted.DispatchClaimOperationId);
        Assert.Equal(p.AdmittedAtUtc, persisted.DispatchAdmittedAtUtc);
        var snapshot = await AdmissionSnapshot(); probe.Dml = 0;
        foreach (var candidate in new[] { new ExperienceAdmissionCandidate(job.Id, p.InstanceId, p.ClaimOperationId), Candidate(job.Id) })
        {
            var repeat = await store.AdmitAsync(candidate, Token);
            Assert.Equal(ExperienceAdmissionState.AlreadyAdmitted, repeat.State); Assert.Null(repeat.Confirmed);
        }
        var serviceRepeat = await new ExperienceAdmissionService(store).AdmitAsync(job.Id, Token);
        Assert.Equal(ExperienceAdmissionState.AlreadyAdmitted, serviceRepeat.State); Assert.Null(serviceRepeat.Permit);
        Assert.Equal(0, probe.Dml); Assert.Equal(snapshot, await AdmissionSnapshot());
    }

    [Theory]
    [InlineData("revoked", ExperienceAuthorizationFailure.Revoked)]
    [InlineData("expired", ExperienceAuthorizationFailure.Expired)]
    [InlineData("future", ExperienceAuthorizationFailure.NotYetValid)]
    [InlineData("inactive-user", ExperienceAuthorizationFailure.InactiveUser)]
    [InlineData("owner", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("project", ExperienceAuthorizationFailure.InactiveContext)]
    [InlineData("client", ExperienceAuthorizationFailure.InactiveContext)]
    [InlineData("terminal", ExperienceAuthorizationFailure.StateConflict)]
    [InlineData("history", ExperienceAuthorizationFailure.HistoryConflict)]
    [InlineData("documents", ExperienceAuthorizationFailure.DocumentsConflict)]
    [InlineData("evidence", ExperienceAuthorizationFailure.DocumentsConflict)]
    [InlineData("source-evidence", ExperienceAuthorizationFailure.DocumentsConflict)]
    [InlineData("mismatch", ExperienceAuthorizationFailure.DocumentsConflict)]
    [InlineData("route", ExperienceAuthorizationFailure.IdentityConflict)]
    [InlineData("metadata", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("version", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("requirement", ExperienceAuthorizationFailure.StateConflict)]
    public async Task Admission_Rejections_ExactFailure_NoWrites(string scenario, ExperienceAuthorizationFailure failure)
    {
        var (s, job) = await AdmissionSetup(scenario == "expired" ? -1 : scenario == "future" ? 86460 : 86400);
        await using (var db = fixture.CreateDbContext())
        {
            switch (scenario)
            {
                case "revoked":
                    Assert.Equal(ExperienceRevocationState.Revoked, (await AdmissionStore().RevokeAsync(job.Id, s.Actor, Token)).State); break;
                case "inactive-user": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET is_active=false WHERE id={s.Actor}", Token); break;
                case "owner": await NewAdmissionOwner(s); break;
                case "project": await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false", Token); break;
                case "client": await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token); break;
                case "terminal":
                    var tracked = await db.RequirementProcessingJobs.SingleAsync(Token); tracked.RequestCancellation(await new JobTerminationTestSupport(fixture).Now()); await db.SaveChangesAsync(Token); break;
                case "history":
                    var historical = RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, job.CreatedAtUtc);
                    historical.RequestCancellation(job.CreatedAtUtc); db.Add(historical); await db.SaveChangesAsync(Token); break;
                case "documents": await db.Database.ExecuteSqlRawAsync("DELETE FROM core.requirement_files", Token); break;
                case "evidence": await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_sha256=NULL,stored_length_bytes=NULL,stored_evidence_version=NULL,stored_evidence_at_utc=NULL", Token); break;
                case "source-evidence": await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submission_files SET stored_length_bytes=NULL,stored_evidence_version=NULL,stored_evidence_at_utc=NULL", Token); break;
                case "mismatch": await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_sha256=repeat('B',64)", Token); break;
                case "route": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET requirement_id={s.OtherRequirement} WHERE id={s.Submission}", Token); break;
                case "metadata": case "version":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET automatic_authorization_policy_version=NULL,authorized_actor_user_id=NULL,authorized_at_utc=NULL,authorization_expires_at_utc=NULL WHERE id={job.Id}", Token);
                    if (scenario == "version") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET required_publication_version={RequirementProcessingJob.PublicationV1} WHERE id={job.Id}", Token);
                    break;
                case "requirement": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirements SET status='Processed' WHERE id={s.Requirement}", Token); break;
            }
        }
        var before = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        var result = await new ExperienceAdmissionService(AdmissionStore(probe)).AdmitAsync(job.Id, Token);
        Assert.Equal(ExperienceAdmissionState.Rejected, result.State); Assert.Equal(failure, result.Failure);
        Assert.Null(result.Permit); Assert.Equal(0, probe.Dml); Assert.Equal(before, await AdmissionSnapshot());
    }

    private async Task<Guid> NewAdmissionOwner(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        var user = global::Domain.Identity.User.CreateFromGoogle("next-owner@example.test", "Next", null, null, DateTimeOffset.UtcNow);
        db.Add(user); await db.SaveChangesAsync(Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={user.Id} WHERE id=(SELECT project_id FROM core.public_experience_submissions WHERE id={s.Submission})", Token);
        return user.Id;
    }

    [Theory]
    [InlineData("plain")] [InlineData("admitted")] [InlineData("expired")] [InlineData("terminal")]
    [InlineData("project")] [InlineData("client")] [InlineData("new-owner")]
    public async Task Admission_Revocation_ExactEffectsAndHistoricalRecognition(string scenario)
    {
        var (s, job) = await AdmissionSetup(scenario == "expired" ? -1 : 86400);
        var actor = s.Actor;
        if (scenario == "admitted") Assert.Equal(ExperienceAdmissionState.Admitted, (await new ExperienceAdmissionService(AdmissionStore()).AdmitAsync(job.Id, Token)).State);
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario == "terminal")
            {
                var j = await db.RequirementProcessingJobs.SingleAsync(Token); j.RequestCancellation(await new JobTerminationTestSupport(fixture).Now()); await db.SaveChangesAsync(Token);
            }
            if (scenario == "project") await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false", Token);
            if (scenario == "client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
        }
        if (scenario == "new-owner")
        {
            actor = await NewAdmissionOwner(s);
            var admission = await new ExperienceAdmissionService(AdmissionStore()).AdmitAsync(job.Id, Token);
            Assert.Equal(ExperienceAuthorizationFailure.NotFound, admission.Failure); Assert.Null(admission.Permit);
            var denied = await AdmissionStore().RevokeAsync(job.Id, s.Actor, Token);
            Assert.Equal(ExperienceAuthorizationFailure.NotFound, denied.Failure); Assert.Null(denied.RevokedAtUtc);
        }
        var before = await AdmissionSnapshot("authorization_revoked_at_utc"); var probe = new AdmissionProbe();
        var service = new RevokeExperienceAuthorizationService(AdmissionStore(probe));
        var result = await service.RevokeAsync(job.Id, actor, Token);
        Assert.Equal(ExperienceRevocationState.Revoked, result.State); Assert.NotNull(result.RevokedAtUtc);
        Assert.Equal(1, probe.Dml); Assert.Equal(before, await AdmissionSnapshot("authorization_revoked_at_utc"));
        var snapshot = await AdmissionSnapshot(); probe.Dml = 0;
        var replay = await service.RevokeAsync(job.Id, actor, Token);
        Assert.Equal(ExperienceRevocationState.AlreadyRevoked, replay.State); Assert.Equal(result.RevokedAtUtc, replay.RevokedAtUtc);
        Assert.Equal(0, probe.Dml); Assert.Equal(snapshot, await AdmissionSnapshot());
    }

    private sealed class AdmissionCommitFailure(bool afterCommit, CancellationTokenSource? caller = null) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!afterCommit) { caller?.Cancel(); throw new IOException("Known precommit failure"); }
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { caller?.Cancel(); throw new IOException("Commit response lost"); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Admission_CommitLoss_NoPermit_NoRetry(bool revoke, bool cancel)
    {
        var (s, job) = await AdmissionSetup(); var before = await AdmissionSnapshot(revoke ? ["authorization_revoked_at_utc"] : DispatchColumns);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var probe = new AdmissionProbe(); var store = AdmissionStore(probe, new AdmissionCommitFailure(true, cancel ? caller : null));
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            { if (revoke) await store.RevokeAsync(job.Id, s.Actor, caller.Token); else await new ExperienceAdmissionService(store).AdmitAsync(job.Id, caller.Token); });
            Assert.Equal(caller.Token, error.CancellationToken);
        }
        else if (revoke)
        {
            var result = await store.RevokeAsync(job.Id, s.Actor, Token);
            Assert.Equal(ExperienceRevocationState.Indeterminate, result.State); Assert.Null(result.RevokedAtUtc);
        }
        else
        {
            var result = await new ExperienceAdmissionService(store).AdmitAsync(job.Id, Token);
            Assert.Equal(ExperienceAdmissionState.Indeterminate, result.State); Assert.Null(result.Permit);
        }
        Assert.Equal(1, probe.Dml); Assert.Equal(before, await AdmissionSnapshot(revoke ? ["authorization_revoked_at_utc"] : DispatchColumns));
        await using var read = fixture.CreateDbContext(); var persisted = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        if (revoke) Assert.NotNull(persisted.AuthorizationRevokedAtUtc); else Assert.NotNull(persisted.DispatchAdmittedAtUtc);
        probe.Dml = 0;
        if (revoke) Assert.Equal(ExperienceRevocationState.AlreadyRevoked, (await AdmissionStore(probe).RevokeAsync(job.Id, s.Actor, Token)).State);
        else Assert.Null((await new ExperienceAdmissionService(AdmissionStore(probe)).AdmitAsync(job.Id, Token)).Permit);
        Assert.Equal(0, probe.Dml);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Admission_RollbackBeforeMutation_AndCallerCancellation(bool revoke)
    {
        var (s, job) = await AdmissionSetup(); var snapshot = await AdmissionSnapshot();
        using var caller = new CancellationTokenSource(); caller.Cancel();
        var probe = new AdmissionProbe(); var connections = new AdmissionConnectionProbe(); var store = AdmissionStore(probe, connections);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        { if (revoke) await store.RevokeAsync(job.Id, s.Actor, caller.Token); else await store.AdmitAsync(Candidate(job.Id), caller.Token); });
        Assert.Equal(caller.Token, error.CancellationToken); Assert.Equal(0, probe.Connections); Assert.Equal(0, connections.Opened);
        Assert.Equal(snapshot, await AdmissionSnapshot());
    }
}
