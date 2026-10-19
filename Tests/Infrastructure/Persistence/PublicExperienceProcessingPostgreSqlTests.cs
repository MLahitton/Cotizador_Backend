using Application.Common.Abstractions.Projects;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Projects;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PublicExperienceProcessingPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentProcessors_OnePair_Replay_NoDownstreamEntities()
    {
        var submission = await SeedAsync();
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var results = await Task.WhenAll(
            Service(first).ExecuteAsync(submission.SubmissionId, Token),
            Service(second).ExecuteAsync(submission.SubmissionId, Token));
        Assert.All(results, result => Assert.Equal("PROCESSED", result!.Status));
        Assert.Single(results.Where(result => result!.IsReplay));
        Assert.Equal(results[0]!.ClientId, results[1]!.ClientId);
        Assert.Equal(results[0]!.ProjectId, results[1]!.ProjectId);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(1, await read.Clients.CountAsync(Token));
        Assert.Equal(1, await read.Projects.CountAsync(Token));
        Assert.False(await read.PreQuotes.AnyAsync(Token));
        Assert.False(await read.Requirements.AnyAsync(Token));
        var persisted = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Equal(results[0]!.ClientId, persisted.ClientId);
        Assert.Equal(results[0]!.ProjectId, persisted.ProjectId);
        Assert.NotNull(persisted.ProcessedAtUtc);
        var owner = await read.Users.SingleAsync(Token);
        Assert.Equal(owner.Id, (await read.Clients.SingleAsync(Token)).CreatedByUserId);
        Assert.Equal(owner.Id, (await read.Projects.SingleAsync(Token)).CreatedByUserId);
    }

    [Fact]
    public async Task ProjectFailure_RollsBackAlreadyInsertedClient_AndPersistsFailure()
    {
        var submission = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        var projects = Substitute.For<IProjectRepository>();
        projects.When(x => x.Add(Arg.Any<Project>())).Do(_ =>
        {
            // Force a real insert inside the outer transaction before failing project creation.
            context.SaveChanges();
            Assert.Equal(1, context.Clients.Count());
            throw new InvalidOperationException("Injected project failure after client insert.");
        });
        var result = await Service(context, projects: projects).ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("FAILED", result!.Status);
        Assert.Equal(PublicExperienceProcessingErrors.ProcessingFailed, result.ErrorCode);
        await AssertNoPairAsync(submission.SubmissionId);
        // The same scoped context must also be safe to retry after rollback.
        var retry = await Service(context).ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("PROCESSED", retry!.Status);
    }

    [Fact]
    public async Task SubmissionUpdateFailure_RollsBackPair_AndPersistsFailure()
    {
        var submission = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION core.fail_public_completion() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF NEW.status = 'PROCESSED' THEN RAISE EXCEPTION 'Injected completion failure'; END IF;
              RETURN NEW;
            END $body$;
            CREATE TRIGGER fail_public_completion BEFORE UPDATE ON core.public_experience_submissions
            FOR EACH ROW EXECUTE FUNCTION core.fail_public_completion();
            """, Token);
        try
        {
            var result = await Service(context).ExecuteAsync(submission.SubmissionId, Token);
            Assert.Equal("FAILED", result!.Status);
            Assert.Equal(PublicExperienceProcessingErrors.ProcessingFailed, result.ErrorCode);
            await AssertNoPairAsync(submission.SubmissionId);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER fail_public_completion ON core.public_experience_submissions;
                DROP FUNCTION core.fail_public_completion();
                """, Token);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("empty")]
    [InlineData("location")]
    public async Task ValidationFailures_ArePersistedWithoutClients(string scenario)
    {
        var submission = await SeedAsync(scenario == "location" ? new string('x', 250) : null);
        await using var context = fixture.CreateDbContext();
        if (scenario == "inactive")
        {
            (await context.Users.SingleAsync(Token)).Deactivate(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(Token);
        }
        var email = scenario switch { "missing" => "missing@example.test", "empty" => "", _ => "owner@example.test" };
        var result = await Service(context, email).ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("FAILED", result!.Status);
        Assert.Equal(scenario == "location" ? PublicExperienceProcessingErrors.LocationInvalid
            : PublicExperienceProcessingErrors.OwnerNotAvailable, result.ErrorCode);
        await AssertNoPairAsync(submission.SubmissionId);
    }

    private async Task<PublicExperienceSubmission> SeedAsync(string? subdivision = null)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        context.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
        var submission = ProcessPublicExperienceSubmissionTests.Submission(subdivision: subdivision);
        context.Add(submission);
        await context.SaveChangesAsync(Token);
        return submission;
    }

    private async Task AssertNoPairAsync(string submissionId)
    {
        await using var context = fixture.CreateDbContext();
        Assert.False(await context.Clients.AnyAsync(Token));
        Assert.False(await context.Projects.AnyAsync(Token));
        var submission = await context.Set<PublicExperienceSubmission>().SingleAsync(x => x.SubmissionId == submissionId, Token);
        Assert.Equal("FAILED", submission.Status);
        Assert.Null(submission.ClientId);
        Assert.Null(submission.ProjectId);
        Assert.Null(submission.ProcessedAtUtc);
        Assert.NotNull(submission.ProcessingErrorCode);
    }

    private static ProcessPublicExperienceSubmissionService Service(ApplicationDbContext context,
        string? email = "owner@example.test", IProjectRepository? projects = null) =>
        new(new PublicExperienceProcessingRepository(context, TimeProvider.System,
                NullLogger<PublicExperienceProcessingRepository>.Instance),
            new IdentityRepository(context), new ClientRepository(context), projects ?? new ProjectRepository(context),
            Options.Create(new PublicExperienceOptions { OwnerEmail = email }), TimeProvider.System);
}
