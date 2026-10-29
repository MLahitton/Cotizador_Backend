using System.Text.Json;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Domain.PreQuotes.ExperienceAuthorizationTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class ExperienceAuthorizationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed record Seed(Guid Actor, Guid Submission, Guid Requirement, Guid OtherRequirement);
    private async Task<Seed> Create()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Authorization", null, null, null, null, null, null, null, user.Id, At);
        var project = Project.Create(client.Id, "AUTH", "Authorization", null, null, user.Id, At);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, At);
        var otherQuote = PreQuote.Create(project.Id, user.Id, "PC-2026-0002", null, At);
        var req = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, At);
        var other = Requirement.Create(otherQuote.Id, user.Id, RequirementCommercialLine.Essential, At);
        var submission = ProcessPublicExperienceSubmissionTests.Submission();
        submission.BeginProcessing(At); submission.CompleteProcessing(client.Id, project.Id, At);
        submission.CompleteRequirementPreparation(quote.Id, req.Id, At);
        db.AddRange(user, client, project, quote, otherQuote, req, other, submission);
        await db.SaveChangesAsync(Token);
        return new(user.Id, submission.Id, req.Id, other.Id);
    }
    private static RequirementProcessingJob Auto(Seed seed, bool other = false) =>
        RequirementProcessingJob.CreateForAutomaticExperience(Guid.NewGuid(), other ? seed.OtherRequirement : seed.Requirement,
            seed.Submission, Guid.NewGuid(), Guid.NewGuid(), seed.Actor, 1, At, At.AddHours(24));
    private async Task Store(RequirementProcessingJob job)
    {
        await using var db = fixture.CreateDbContext(); db.Add(job); await db.SaveChangesAsync(Token);
    }
    private async Task<string> Jobs()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(await db.RequirementProcessingJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token));
    }
    private async Task<string> Business()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new {
            Requirements = await db.Requirements.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Quotes = await db.PreQuotes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Attempts = await db.RequirementProcessingAttempts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Claims = await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token) });
    }
    private async Task Sql(string sql, Guid job, Guid actor = default)
    {
        await using var c = new NpgsqlConnection(fixture.ConnectionString); await c.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, c);
        command.Parameters.AddWithValue("job", job); command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(Token);
    }

    [Theory]
    [InlineData("UTC")] [InlineData("America/New_York")] [InlineData("Europe/Berlin")]
    public async Task Roundtrip_Elapsed24Hours_AcrossSessionTimezones(string zone)
    {
        var s = await Create(); var job = Auto(s); var before = await Business();
        job.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At);
        job.RevokeAutomaticAuthorization(At.AddDays(3));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.OpenConnectionAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('TimeZone', {zone}, false)", Token);
            db.Add(job); await db.SaveChangesAsync(Token);
            var seconds = await db.Database.SqlQuery<decimal>($"SELECT extract(epoch from authorization_expires_at_utc - authorized_at_utc) AS \"Value\" FROM core.requirement_processing_jobs WHERE id={job.Id}").SingleAsync(Token);
            Assert.Equal(86400m, seconds);
        }
        await using var read = fixture.CreateDbContext();
        var actual = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(JsonSerializer.Serialize(job), JsonSerializer.Serialize(actual));
        Assert.Equal(before, await Business());
        Assert.Empty(await read.JobLeaseOperations.ToListAsync(Token));
        Assert.Equal(RequirementProcessingJobState.Queued, actual.State);
    }

    [Theory]
    [InlineData("AutomaticAuthorizationPolicyVersion")] [InlineData("AuthorizedActorUserId")]
    [InlineData("AuthorizedAtUtc")] [InlineData("AuthorizationExpiresAtUtc")]
    public async Task EmissionEfThrow_ExactProperty_NoDml_IndependentRead(string property)
    {
        var s = await Create(); var job = Auto(s); await Store(job);
        var before = await Jobs(); var business = await Business(); var probe = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(new JobTerminationTestSupport(fixture).Options(probe));
        var tracked = await db.RequirementProcessingJobs.SingleAsync(Token);
        db.Entry(tracked).Property(property).CurrentValue = property switch {
            "AutomaticAuthorizationPolicyVersion" => 2, "AuthorizedActorUserId" => Guid.NewGuid(),
            "AuthorizedAtUtc" => At.AddTicks(10), _ => At.AddHours(24).AddTicks(10) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        Assert.Contains(property, error.Message); Assert.Equal(0, probe.Count);
        Assert.Equal(before, await Jobs()); Assert.Equal(business, await Business());
        output.WriteLine($"{property}: InvalidOperationException, DML={probe.Count}, independent snapshots intact");
    }

    [Fact]
    public async Task LocalMarksPersist_RevokedAfterExpiryAndTerminal_NoBusinessEffects()
    {
        var s = await Create(); var job = Auto(s); await Store(job); var before = await Business();
        await using (var db = fixture.CreateDbContext())
        {
            var j = await db.RequirementProcessingJobs.SingleAsync(Token);
            j.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At.AddTicks(10));
            j.RequestCancellation(At.AddHours(1)); j.RevokeAutomaticAuthorization(At.AddDays(2));
            await db.SaveChangesAsync(Token);
        }
        var terminal = await Jobs();
        await using (var db = fixture.CreateDbContext())
        {
            var j = await db.RequirementProcessingJobs.SingleAsync(Token);
            j.RevokeAutomaticAuthorization(At.AddDays(3)); await db.SaveChangesAsync(Token);
        }
        Assert.Equal(terminal, await Jobs()); Assert.Equal(before, await Business());
    }

    public static IEnumerable<object[]> InvalidShapes()
    {
        const string auth = "ck_processing_jobs_automatic_authorization";
        const string dispatch = "ck_processing_jobs_automatic_dispatch";
        const string revoked = "ck_processing_jobs_automatic_revocation";
        foreach (var column in new[] { "automatic_authorization_policy_version", "authorized_actor_user_id", "authorized_at_utc", "authorization_expires_at_utc" })
            yield return new object[] { column + "=NULL", auth };
        foreach (var assignment in new[] { "automatic_authorization_policy_version=2", "authorized_actor_user_id='00000000-0000-0000-0000-000000000000'",
            "submission_id=NULL", "submission_id='00000000-0000-0000-0000-000000000000'", "trigger_kind='ManualInitial'",
            "required_publication_version='sng-job-publication-v1'", "requested_by_user_id=@actor",
            "authorization_expires_at_utc=authorized_at_utc+interval '23 hours'",
            "authorization_expires_at_utc=authorized_at_utc+interval '25 hours'" })
            yield return new object[] { assignment, auth };
        foreach (var date in new[] { "authorized_at_utc", "authorization_expires_at_utc" })
            foreach (var infinity in new[] { "infinity", "-infinity" })
                yield return new object[] { $"{date}='{infinity}'", auth };
        foreach (var assignment in new[] { "dispatch_admitted_at_utc=authorized_at_utc", "dispatch_instance_id=@actor", "dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc=authorized_at_utc,dispatch_instance_id=@actor",
            "dispatch_admitted_at_utc=authorized_at_utc,dispatch_claim_operation_id=@actor",
            "dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc=authorization_expires_at_utc,dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc=authorized_at_utc-interval '1 microsecond',dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc='infinity',dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc='-infinity',dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc=authorized_at_utc,dispatch_instance_id='00000000-0000-0000-0000-000000000000',dispatch_claim_operation_id=@actor",
            "dispatch_admitted_at_utc=authorized_at_utc,dispatch_instance_id=@actor,dispatch_claim_operation_id='00000000-0000-0000-0000-000000000000'" })
            yield return new object[] { assignment, dispatch };
        foreach (var assignment in new[] { "authorization_revoked_at_utc=authorized_at_utc-interval '1 microsecond'",
            "authorization_revoked_at_utc='infinity'", "authorization_revoked_at_utc='-infinity'",
            "dispatch_admitted_at_utc=authorized_at_utc+interval '1 second',dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor,authorization_revoked_at_utc=authorized_at_utc" })
            yield return new object[] { assignment, revoked };
    }

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public async Task Checks_RejectPartialGroupsAndInvalidValues(string assignments, string constraint)
    {
        var s = await Create(); var job = Auto(s); await Store(job); var before = await Jobs();
        var e = await Assert.ThrowsAsync<PostgresException>(() => Sql($"UPDATE core.requirement_processing_jobs SET {assignments} WHERE id=@job", job.Id, s.Actor));
        Assert.Equal("23514", e.SqlState); Assert.Equal(constraint, e.ConstraintName);
        Assert.Equal(before, await Jobs());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NoAuthorization_CannotHaveDispatchOrRevocation(bool dispatch)
    {
        var s = await Create(); var job = RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, At);
        await Store(job); var before = await Jobs();
        var assignments = dispatch ? "dispatch_admitted_at_utc=created_at_utc,dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor" : "authorization_revoked_at_utc=created_at_utc";
        var e = await Assert.ThrowsAsync<PostgresException>(() => Sql($"UPDATE core.requirement_processing_jobs SET {assignments} WHERE id=@job", job.Id, s.Actor));
        Assert.Equal("23514", e.SqlState);
        Assert.Equal(dispatch ? "ck_processing_jobs_automatic_dispatch" : "ck_processing_jobs_automatic_revocation", e.ConstraintName);
        Assert.Equal(before, await Jobs());
    }

    [Fact]
    public async Task AllFourEmissionFields_RejectEveryPartialNullGroup()
    {
        var s = await Create(); var job = Auto(s); await Store(job); var before = await Jobs();
        var columns = new[] { "automatic_authorization_policy_version", "authorized_actor_user_id", "authorized_at_utc", "authorization_expires_at_utc" };
        for (var mask = 1; mask < 15; mask++)
        {
            var assignments = string.Join(",", columns.Where((_, i) => (mask & (1 << i)) != 0).Select(c => c + "=NULL"));
            var error = await Assert.ThrowsAsync<PostgresException>(() => Sql($"UPDATE core.requirement_processing_jobs SET {assignments} WHERE id=@job", job.Id));
            Assert.Equal("23514", error.SqlState); Assert.Equal("ck_processing_jobs_automatic_authorization", error.ConstraintName);
            Assert.Equal(before, await Jobs());
        }
    }

    [Fact]
    public async Task CalendarDayAcrossDst_IsNotElapsed24Hours()
    {
        var s = await Create(); var job = Auto(s); await Store(job); var before = await Jobs();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using (var zone = new NpgsqlCommand("SET TIME ZONE 'America/New_York'", connection)) await zone.ExecuteNonQueryAsync(Token);
        await using (var difference = new NpgsqlCommand("SELECT extract(epoch from (authorized_at_utc + interval '1 day') - authorized_at_utc) FROM core.requirement_processing_jobs WHERE id=@job", connection))
        {
            difference.Parameters.AddWithValue("job", job.Id);
            Assert.Equal(82800m, (decimal)(await difference.ExecuteScalarAsync(Token))!);
        }
        await using var update = new NpgsqlCommand("UPDATE core.requirement_processing_jobs SET authorization_expires_at_utc=authorized_at_utc+interval '1 day' WHERE id=@job", connection);
        update.Parameters.AddWithValue("job", job.Id);
        var e = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync(Token));
        Assert.Equal("23514", e.SqlState); Assert.Equal("ck_processing_jobs_automatic_authorization", e.ConstraintName);
        Assert.Equal(before, await Jobs());
    }

    [Fact]
    public async Task ActorForeignKeyRestrict_AndPermanentSubmissionUniqueness()
    {
        var s = await Create(); var job = Auto(s); job.RequestCancellation(At); await Store(job);
        var before = await Jobs(); var business = await Business();
        var fk = await Assert.ThrowsAsync<PostgresException>(() => Sql("UPDATE core.requirement_processing_jobs SET authorized_actor_user_id=@actor WHERE id=@job", job.Id, Guid.NewGuid()));
        Assert.Equal("23503", fk.SqlState);
        Assert.Equal("FK_requirement_processing_jobs_users_authorized_actor_user_id", fk.ConstraintName);
        await using (var db = fixture.CreateDbContext())
        {
            var restrictions = await db.Database.SqlQuery<string>($"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conrelid='core.requirement_processing_jobs'::regclass AND conname={fk.ConstraintName}").SingleAsync(Token);
            Assert.Equal("r", restrictions);
        }
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => Store(Auto(s, true)));
        var pg = Assert.IsType<PostgresException>(duplicate.InnerException);
        Assert.Equal("23505", pg.SqlState); Assert.Equal("ux_processing_jobs_automatic_submission", pg.ConstraintName);
        Assert.Equal(before, await Jobs()); Assert.Equal(business, await Business());
        // The partial index does not retroactively authorize or exclude a historical job.
        await Store(RequirementProcessingJob.Create(s.OtherRequirement, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.PublicPreparation, At, s.Submission));
    }

    [Fact]
    public async Task DirectSqlLimits_NoClaimForeignKey_NoOperationalAuthority()
    {
        var s = await Create(); var job = Auto(s); await Store(job); var business = await Business();
        await Sql("UPDATE core.requirement_processing_jobs SET dispatch_admitted_at_utc=authorized_at_utc,dispatch_instance_id=@actor,dispatch_claim_operation_id=@actor,authorization_revoked_at_utc=authorization_expires_at_utc WHERE id=@job", job.Id, s.Actor);
        await Sql("UPDATE core.requirement_processing_jobs SET dispatch_admitted_at_utc=NULL,dispatch_instance_id=NULL,dispatch_claim_operation_id=NULL,authorization_revoked_at_utc=NULL WHERE id=@job", job.Id);
        Assert.Equal(business, await Business());
        await Sql("DELETE FROM core.requirement_processing_jobs WHERE id=@job", job.Id);
        Assert.Equal(business, await Business());
    }

    [Fact]
    public async Task ConcurrentInsert_IndependentPids_ObservedLock_OneStructuralIntent()
    {
        var s = await Create(); var before = await Business();
        await using var winner = fixture.CreateDbContext(); await using var loser = fixture.CreateDbContext();
        await winner.Database.OpenConnectionAsync(Token); await loser.Database.OpenConnectionAsync(Token);
        var winPid = ((NpgsqlConnection)winner.Database.GetDbConnection()).ProcessID;
        var losePid = ((NpgsqlConnection)loser.Database.GetDbConnection()).ProcessID;
        Assert.NotEqual(winPid, losePid);
        await using var tx = await winner.Database.BeginTransactionAsync(Token);
        winner.Add(Auto(s)); await winner.SaveChangesAsync(Token);
        loser.Add(Auto(s, true)); var pending = loser.SaveChangesAsync(Token); var committed = false;
        try
        {
            await new JobProtocolTestSupport(fixture).WaitBlocked(losePid, winPid);
            output.WriteLine($"Automatic INSERT winner={winPid}, blocked={losePid}; Lock/pg_blocking_pids observed before commit");
            await tx.CommitAsync(Token); committed = true;
            var ex = await Assert.ThrowsAsync<DbUpdateException>(async () => await pending);
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("23505", pg.SqlState); Assert.Equal("ux_processing_jobs_automatic_submission", pg.ConstraintName);
        }
        finally
        {
            if (!committed) await tx.RollbackAsync(CancellationToken.None);
            try { await pending; } catch (DbUpdateException) { }
        }
        await using var read = fixture.CreateDbContext();
        var only = Assert.Single(await read.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token));
        Assert.Equal(RequirementProcessingJobState.Queued, only.State); Assert.Equal(0, only.AttemptCount);
        Assert.Null(only.DispatchAdmittedAtUtc); Assert.Equal(before, await Business());
    }
}
