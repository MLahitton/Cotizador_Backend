using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class RequirementProcessingJobPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static RequirementProcessingJob Job(Guid requirement, Guid? request = null) =>
        RequirementProcessingJob.Create(requirement, request ?? Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, Now);

    private async Task<(Guid First, Guid Second, Guid Owner, Guid Submission)> SeedAsync()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Job test", null, null, null, null, null, null, null, user.Id, Now);
        var project = Project.Create(client.Id, "JOB-TEST", "Job test", null, null, user.Id, Now);
        var quote1 = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, Now);
        var quote2 = PreQuote.Create(project.Id, user.Id, "PC-2026-0002", null, Now);
        var first = Requirement.Create(quote1.Id, user.Id, RequirementCommercialLine.Essential, Now);
        var second = Requirement.Create(quote2.Id, user.Id, RequirementCommercialLine.Essential, Now);
        var submission = ProcessPublicExperienceSubmissionTests.Submission();
        db.AddRange(user, client, project, quote1, quote2, first, second, submission);
        await db.SaveChangesAsync(Token);
        return (first.Id, second.Id, user.Id, submission.Id);
    }

    [Fact]
    public async Task RoundTrip_TerminalHistoryAndExplicitRetry_DistinctRequirementsCanRun()
    {
        var ids = await SeedAsync();
        var first = RequirementProcessingJob.Create(ids.First, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.PublicPreparation, Now, ids.Submission);
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(first, Job(ids.Second));
            await db.SaveChangesAsync(Token);
        }
        await using (var db = fixture.CreateDbContext())
        {
            var saved = await db.RequirementProcessingJobs.SingleAsync(x => x.Id == first.Id, Token);
            Assert.Equal(first.ExecutionRequestId, saved.ExecutionRequestId);
            Assert.Equal(first.CorrelationId, saved.CorrelationId);
            Assert.Equal(ids.Submission, saved.SubmissionId);
            Assert.Null(saved.RequestedByUserId);
            saved.AssignExecution("worker-test", Now, Now.AddMinutes(1));
            saved.Fail("TEST_FAILURE", Now.AddSeconds(1));
            await db.SaveChangesAsync(Token);
            var retry = RequirementProcessingJob.Create(ids.First, Guid.NewGuid(), Guid.NewGuid(),
                RequirementProcessingJobTrigger.ManualRetry, Now.AddSeconds(2), ids.Submission, ids.Owner, first.Id);
            db.Add(retry);
            await db.SaveChangesAsync(Token);
        }
        await using var read = fixture.CreateDbContext();
        var jobs = await read.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token);
        Assert.Equal(3, jobs.Count);
        var terminal = Assert.Single(jobs, x => x.Id == first.Id);
        Assert.Equal(RequirementProcessingJobState.Failed, terminal.State);
        Assert.Equal("TEST_FAILURE", terminal.LastErrorCode);
        Assert.Null(terminal.LeaseOwner);
        Assert.Null(terminal.LeaseExpiresAtUtc);
        Assert.Equal(1, terminal.LeaseGeneration);
        var retried = Assert.Single(jobs, x => x.RetryOfJobId == first.Id);
        Assert.Equal(0, retried.AttemptCount);
        Assert.Equal(ids.Owner, retried.RequestedByUserId);
        Assert.Equal(ids.Submission, retried.SubmissionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentActiveJobs_TwoConnections_ExactlyOneIndexWinner(bool running)
    {
        var ids = await SeedAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<(bool Won, int Pid)> InsertAsync()
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.OpenConnectionAsync(Token);
            var pid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
            var job = Job(ids.First);
            if (running) job.AssignExecution("worker-test", Now, Now.AddMinutes(1));
            db.Add(job);
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
            await gate.Task.WaitAsync(Token);
            try
            {
                await db.SaveChangesAsync(Token);
                return (true, pid);
            }
            catch (DbUpdateException error)
            {
                var pg = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
                Assert.Equal("ux_processing_jobs_active_requirement", pg.ConstraintName);
                return (false, pid);
            }
        }
        var a = InsertAsync();
        var b = InsertAsync();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        gate.SetResult();
        var results = await Task.WhenAll(a, b);
        Assert.NotEqual(results[0].Pid, results[1].Pid);
        Assert.Single(results, x => x.Won);
        Assert.Single(results, x => !x.Won);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(1, await read.RequirementProcessingJobs.CountAsync(x => x.RequirementId == ids.First
            && (x.State == RequirementProcessingJobState.Queued || x.State == RequirementProcessingJobState.Running), Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestIdentityRemainsGloballyUniqueAfterTerminal(bool differentRequirement)
    {
        var ids = await SeedAsync();
        var first = Job(ids.First);
        first.RequestCancellation(Now);
        await using var db = fixture.CreateDbContext();
        db.Add(first);
        await db.SaveChangesAsync(Token);
        db.Add(Job(differentRequirement ? ids.Second : ids.First, first.ExecutionRequestId));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("ux_processing_jobs_execution_request", pg.ConstraintName);
    }

    [Fact]
    public async Task AttemptCannotBeLinkedToTwoJobs()
    {
        var ids = await SeedAsync();
        var attempt = RequirementProcessingAttempt.Create(ids.First, ids.Owner, Guid.NewGuid(), Now);
        attempt.Start(Now);
        var first = Job(ids.First);
        first.AssignExecution("worker-test", Now, Now.AddMinutes(1));
        first.LinkStartedAttempt(attempt.Id);
        first.Succeed(Now.AddSeconds(1));
        await using var db = fixture.CreateDbContext();
        db.AddRange(attempt, first);
        await db.SaveChangesAsync(Token);
        var second = Job(ids.First);
        second.AssignExecution("worker-test", Now, Now.AddMinutes(1));
        second.LinkStartedAttempt(attempt.Id);
        db.Add(second);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("ux_processing_jobs_attempt", pg.ConstraintName);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    [Theory]
    [InlineData("attempt_count = 2", "ck_processing_jobs_attempt")]
    [InlineData("execution_request_id = '00000000-0000-0000-0000-000000000000'", "ck_processing_jobs_identity")]
    [InlineData("correlation_id = '00000000-0000-0000-0000-000000000000'", "ck_processing_jobs_identity")]
    [InlineData("trigger_kind = 'ManualRetry'", "ck_processing_jobs_trigger")]
    [InlineData("trigger_kind = 'Other'", "ck_processing_jobs_trigger")]
    [InlineData("state = 'Other'", "ck_processing_jobs_lifecycle")]
    [InlineData("lease_generation = 1", "ck_processing_jobs_lifecycle")]
    [InlineData("last_error_code = 'Private exception'", "ck_processing_jobs_error")]
    [InlineData("cancellation_requested_at_utc = created_at_utc - interval '1 second'", "ck_processing_jobs_dates")]
    public async Task DatabaseRejectsInvalidLocalCombinations(string assignment, string constraint)
    {
        var ids = await SeedAsync();
        await using var db = fixture.CreateDbContext();
        var job = Job(ids.First);
        // Running permits cancellation timestamps, isolating the date check from queue rules.
        if (constraint == "ck_processing_jobs_dates")
            job.AssignExecution("worker-test", Now, Now.AddMinutes(1));
        db.Add(job);
        await db.SaveChangesAsync(Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE core.requirement_processing_jobs SET " + assignment, Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    [Theory]
    [InlineData("requirement_id")]
    [InlineData("submission_id")]
    [InlineData("requested_by_user_id")]
    [InlineData("processing_attempt_id")]
    [InlineData("retry_of_job_id")]
    public async Task MissingReferencesAreRejected(string column)
    {
        var ids = await SeedAsync();
        await using var db = fixture.CreateDbContext();
        var job = Job(ids.First);
        job.AssignExecution("worker-test", Now, Now.AddMinutes(1));
        db.Add(job);
        await db.SaveChangesAsync(Token);
        var extra = column == "processing_attempt_id" ? ", attempt_count = 1"
            : column == "retry_of_job_id" ? ", trigger_kind = 'ManualRetry'" : "";
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE core.requirement_processing_jobs SET " + column + " = {0}" + extra, [Guid.NewGuid()], Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task ReferencedRecordsUseRestrictDelete()
    {
        var ids = await SeedAsync();
        await using var db = fixture.CreateDbContext();
        var job = Job(ids.First);
        db.Add(job);
        await db.SaveChangesAsync(Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM core.requirements WHERE id = {ids.First}", Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        var entity = db.Model.FindEntityType(typeof(RequirementProcessingJob))!;
        Assert.Equal(5, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
}
