using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Domain.PreQuotes.ExperienceAuthorizationTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExperienceAuthorizationPostgreSqlTests
{
    [Fact]
    public async Task ActorDelete_RestrictConstraintExact()
    {
        var s = await Create(); var job = Auto(s); await Store(job);
        var actor = User.CreateFromGoogle("isolated-actor@test.local", "Actor", null, null, At);
        await using (var db = fixture.CreateDbContext()) { db.Add(actor); await db.SaveChangesAsync(Token); }
        await Sql("UPDATE core.requirement_processing_jobs SET authorized_actor_user_id=@actor WHERE id=@job", job.Id, actor.Id);
        var before = await Jobs();
        var e = await Assert.ThrowsAsync<PostgresException>(() => Sql("DELETE FROM identity.users WHERE id=@actor", job.Id, actor.Id));
        Assert.Equal("23503", e.SqlState); Assert.Equal("FK_requirement_processing_jobs_users_authorized_actor_user_id", e.ConstraintName);
        Assert.Equal(before, await Jobs());
        await using var read = fixture.CreateDbContext(); Assert.True(await read.Set<User>().AnyAsync(x => x.Id == actor.Id, Token));
    }

    [Fact]
    public async Task UpgradeFromImmediatePredecessor_AllHistoryIntact_MetadataNull_NoPendingModel()
    {
        var s = await Create();
        await using (var db = fixture.CreateDbContext())
        {
            var first = await db.Requirements.SingleAsync(x => x.Id == s.Requirement, Token);
            var projectId = (await db.PreQuotes.SingleAsync(x => x.Id == first.PreQuoteId, Token)).ProjectId;
            for (var i = 0; i < 4; i++)
            {
                var quote = PreQuote.Create(projectId, s.Actor, $"PC-2026-{i + 3:D4}", null, At);
                var req = Requirement.Create(quote.Id, s.Actor, RequirementCommercialLine.Essential, At);
                var v2 = i >= 2; var succeeded = i % 2 == 1;
                var job = v2 ? RequirementProcessingJob.CreateForVerifiedPublication(req.Id, Guid.NewGuid(), Guid.NewGuid(),
                    RequirementProcessingJobTrigger.ManualInitial, At) : RequirementProcessingJob.Create(req.Id, Guid.NewGuid(), Guid.NewGuid(),
                    RequirementProcessingJobTrigger.ManualInitial, At);
                var claimId = Guid.NewGuid(); var owner = $"exec:{Guid.NewGuid():D}:{claimId:D}";
                var claim = JobLeaseOperation.CreateClaim(claimId, job.Id, req.Id, job.ExecutionRequestId, s.Actor,
                    claimId, owner, 1, 1, At, At, At.AddSeconds(120));
                Guid? attemptId = null;
                var file = RequirementFile.CreateWithStoredEvidence(req.Id, "control.pdf", "application/pdf", 3,
                    $"tests/auth-history-{i}", At, new string('A', 64), 3, At, 1);
                if (succeeded)
                {
                    job.AssignExecution(owner, At, At.AddSeconds(120));
                    var attempt = RequirementProcessingAttempt.Create(req.Id, s.Actor, job.CorrelationId, At);
                    attempt.Start(At); req.StartProcessing(At); job.LinkStartedAttempt(attempt.Id); attemptId = attempt.Id;
                    attempt.Complete(DocumentProcessingOutcome.Completed, At.AddSeconds(10)); req.MarkProcessed(At.AddSeconds(10));
                    job.SucceedWithPublicationReceipt(job.RequiredPublicationVersion, new string('B', 64), At.AddSeconds(10));
                    var extraction = RequirementExtractionResult.Create(attempt.Id, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, At.AddSeconds(10));
                    var proposal = RequirementTechnicalProposal.Create(req.Id, extraction.Id, attempt.Id, false, At.AddSeconds(10));
                    var evidenceId = Guid.NewGuid();
                    var evidence = RequirementExecutionEvidence.Create(evidenceId, req.Id, job.Id, job.ExecutionRequestId, attempt.Id, 1,
                        s.Actor, file.Id, 1, At, At.AddSeconds(1), 1, new[] {
                            RequirementExecutionDocument.Create(evidenceId, file.Id, 0, file.OriginalFileName, file.ContentType, file.StorageKey,
                                At, 3, new string('A', 64), 3, At, 1, new string('A', 64), 3) });
                    db.AddRange(attempt, extraction, proposal, evidence);
                }
                var renew = JobLeaseOperation.CreateRenew(Guid.NewGuid(), job.Id, req.Id, job.ExecutionRequestId, s.Actor, claimId,
                    owner, 1, 1, At.AddSeconds(120), attemptId, At, At.AddSeconds(1), At.AddSeconds(121));
                if (i == 0)
                    db.Add(ExpiredExecutionCancellation.Create(Guid.NewGuid(), job.Id, req.Id, job.ExecutionRequestId, claimId,
                        s.Actor, job.CorrelationId, owner, 1, At, At.AddSeconds(120), null, At, At.AddSeconds(120), At.AddSeconds(120), 1));
                db.AddRange(quote, req, job, file, claim, renew);
            }
            await db.SaveChangesAsync(Token);
        }
        // Structural history, including a closure receipt: not operational Claim/Renew/closure/publication.
        await using var migration = fixture.CreateDbContext();
        var migrator = migration.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027195507_AddExpiredExecutionCancellations", Token);
        try
        {
            var before = await RawHistory();
            await migrator.MigrateAsync(cancellationToken: Token);
            Assert.Equal(before, await RawHistory());
            await using var read = fixture.CreateDbContext();
            Assert.False(read.Database.HasPendingModelChanges());
            var jobs = await read.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token);
            Assert.Equal(4, jobs.Count);
            Assert.All(jobs, j => {
                Assert.Null(j.AutomaticAuthorizationPolicyVersion); Assert.Null(j.AuthorizedActorUserId);
                Assert.Null(j.AuthorizedAtUtc); Assert.Null(j.AuthorizationExpiresAtUtc); Assert.Null(j.AuthorizationRevokedAtUtc);
                Assert.Null(j.DispatchAdmittedAtUtc); Assert.Null(j.DispatchInstanceId); Assert.Null(j.DispatchClaimOperationId);
            });
            Assert.Equal(8, await read.JobLeaseOperations.CountAsync(Token));
            Assert.Equal(1, await read.ExpiredExecutionCancellations.CountAsync(Token));
            Assert.Equal(2, await read.RequirementProcessingAttempts.CountAsync(Token));
            Assert.Equal(2, await read.RequirementTechnicalProposals.CountAsync(Token));
            Assert.Equal(2, jobs.Count(j => j.PublicationContractVersion is not null));
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }

    private async Task<string> RawHistory()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        var result = new List<string>();
        foreach (var table in new[] { "requirement_processing_jobs", "requirements", "pre_quotes", "requirement_processing_attempts",
            "requirement_files", "requirement_extraction_results", "requirement_technical_proposals", "requirement_execution_evidence",
            "requirement_execution_documents", "job_lease_operations", "expired_execution_cancellations", "public_experience_submissions" })
        {
            // Fixed identifiers only; omit newly added null columns for cross-schema comparison.
            var sql = $"SELECT coalesce(jsonb_agg(v ORDER BY v::text),'[]'::jsonb)::text FROM (SELECT to_jsonb(t) - ARRAY['automatic_authorization_policy_version','authorized_actor_user_id','authorized_at_utc','authorization_expires_at_utc','authorization_revoked_at_utc','dispatch_admitted_at_utc','dispatch_instance_id','dispatch_claim_operation_id'] AS v FROM core.{table} t) s";
            await using var command = new NpgsqlCommand(sql, connection); result.Add((string)(await command.ExecuteScalarAsync(Token))!);
        }
        return string.Join("\n", result);
    }
}
