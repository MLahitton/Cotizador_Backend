using Application.Common.Abstractions.PreQuotes;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class RequirementRepositoryTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindByIdAsync_WithPersistedRequirement_ReturnsRequirement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var requirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(requirement);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var result = await new RequirementRepository(readContext)
            .FindByIdAsync(requirement.Id, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(requirement.Id, result!.Id);
        Assert.Equal(seeded.PreQuoteId, result.PreQuoteId);
        Assert.Equal(seeded.UserId, result.CreatedByUserId);
        Assert.Equal(RequirementStatus.Pending, result.Status);
        Assert.Equal(RequirementCommercialLine.Essential, result.CommercialLine);
        Assert.True(result.IsActive);
        Assert.Empty(result.Files);
        Assert.Empty(result.ProcessingAttempts);
    }

    [Fact]
    public async Task Requirements_WithSamePreQuote_CanPersistMultipleRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var first = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var second = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(2));

        await using (var context = fixture.CreateDbContext())
        {
            context.Requirements.AddRange(first, second);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using var verification = fixture.CreateDbContext();
        var count = await verification.Requirements.CountAsync(
            requirement => requirement.PreQuoteId == seeded.PreQuoteId,
            cancellationToken);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ListFilesByRequirementIdAsync_ReturnsOnlyRequirementFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var firstRequirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var secondRequirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(2));
        var firstFile = RequirementFile.Create(
            firstRequirement.Id,
            "first.pdf",
            "application/pdf",
            100,
            "requirements/first/original.pdf",
            At.AddMinutes(3));
        var secondFile = RequirementFile.Create(
            firstRequirement.Id,
            "second.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            200,
            "requirements/first/original.xlsx",
            At.AddMinutes(4));
        var otherFile = RequirementFile.Create(
            secondRequirement.Id,
            "other.pdf",
            "application/pdf",
            300,
            "requirements/second/original.pdf",
            At.AddMinutes(5));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(firstRequirement);
            repository.Add(secondRequirement);
            repository.AddFile(firstFile);
            repository.AddFile(secondFile);
            repository.AddFile(otherFile);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var files = await new RequirementRepository(readContext)
            .ListFilesByRequirementIdAsync(
                firstRequirement.Id,
                cancellationToken);

        Assert.Equal(2, files.Count);
        Assert.Equal(
            [firstFile.Id, secondFile.Id],
            files.Select(file => file.Id).ToArray());
        Assert.All(files, file =>
            Assert.Equal(firstRequirement.Id, file.RequirementId));
        Assert.Equal("application/pdf", files[0].ContentType);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files[1].ContentType);
        Assert.Equal(100, files[0].SizeBytes);
        Assert.Equal("requirements/first/original.xlsx", files[1].StorageKey);
    }

    [Fact]
    public async Task FindByIdAsync_IncludesFilesAndAttempts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var requirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var file = RequirementFile.Create(
            requirement.Id,
            "source.pdf",
            "application/pdf",
            100,
            "requirements/include/source.pdf",
            At.AddMinutes(2));
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(3));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(requirement);
            repository.AddFile(file);
            repository.AddProcessingAttempt(attempt);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var result = await new RequirementRepository(readContext)
            .FindByIdAsync(requirement.Id, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(file.Id, Assert.Single(result!.Files).Id);
        Assert.Equal(
            attempt.Id,
            Assert.Single(result.ProcessingAttempts).Id);
    }

    [Fact]
    public async Task FindProcessingAttemptByIdAsync_ReturnsPersistedAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var firstRequirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var secondRequirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(2));
        var completed = RequirementProcessingAttempt.Create(
            firstRequirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(3));
        completed.Start(At.AddMinutes(4));
        completed.Complete(
            DocumentProcessingOutcome.RequiresReview,
            At.AddMinutes(5));
        var failed = RequirementProcessingAttempt.Create(
            firstRequirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(6));
        failed.Start(At.AddMinutes(7));
        failed.Fail("AI_INVALID_RESPONSE", At.AddMinutes(8));
        var other = RequirementProcessingAttempt.Create(
            secondRequirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(9));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(firstRequirement);
            repository.Add(secondRequirement);
            repository.AddProcessingAttempt(completed);
            repository.AddProcessingAttempt(failed);
            repository.AddProcessingAttempt(other);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var result = await new RequirementRepository(readContext)
            .FindProcessingAttemptByIdAsync(
                failed.Id,
                cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(firstRequirement.Id, result!.RequirementId);
        Assert.Equal(DocumentProcessingState.Finished, result.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, result.Outcome);
        Assert.Equal("AI_INVALID_RESPONSE", result.ErrorCode);
        Assert.Equal(At.AddMinutes(7), result.StartedAtUtc);
        Assert.Equal(At.AddMinutes(8), result.CompletedAtUtc);

        await using var verification = fixture.CreateDbContext();
        var firstRequirementAttemptCount =
            await verification.RequirementProcessingAttempts.CountAsync(
                attempt => attempt.RequirementId == firstRequirement.Id,
                cancellationToken);
        Assert.Equal(2, firstRequirementAttemptCount);
    }

    [Fact]
    public async Task ProcessingAttempt_WithPendingOutcomeNull_RoundTrips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var requirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(2));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(requirement);
            repository.AddProcessingAttempt(attempt);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var result = await new RequirementRepository(readContext)
            .FindProcessingAttemptByIdAsync(
                attempt.Id,
                cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(DocumentProcessingState.Pending, result!.ProcessingState);
        Assert.Null(result.Outcome);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.StartedAtUtc);
        Assert.Null(result.CompletedAtUtc);
    }

    [Fact]
    public async Task FinalizeProcessingFailureAsync_ClearsTrackedChangesAndPersistsFailedState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var requirement = Requirement.Create(seeded.PreQuoteId, seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(1));
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(2));
        attempt.Start(At.AddMinutes(3));
        requirement.StartProcessing(At.AddMinutes(3));

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            repository.Add(requirement);
            repository.AddProcessingAttempt(attempt);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using (var dirtyContext = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(dirtyContext);
            repository.Add(Requirement.Create(Guid.NewGuid(), seeded.UserId, RequirementCommercialLine.Essential, At.AddMinutes(4)));

            var finalization =
                await repository.FinalizeProcessingFailureAsync(
                    requirement.Id,
                    attempt.Id,
                    "REQUIREMENT_PERSISTENCE_ERROR",
                    At.AddMinutes(5),
                    cancellationToken);

            Assert.NotNull(finalization);
            Assert.Equal(requirement.Id, finalization!.RequirementId);
            Assert.Equal(attempt.Id, finalization.ProcessingAttemptId);
            Assert.Equal(
                DocumentProcessingState.Finished,
                finalization.ProcessingState);
            Assert.Equal(DocumentProcessingOutcome.Failed, finalization.Outcome);
            Assert.Equal(
                "REQUIREMENT_PERSISTENCE_ERROR",
                finalization.ErrorCode);
            Assert.Equal(At.AddMinutes(3), finalization.StartedAtUtc);
            Assert.Equal(At.AddMinutes(5), finalization.CompletedAtUtc);
        }

        await using var verification = fixture.CreateDbContext();
        var persistedRequirement = await verification.Requirements
            .SingleAsync(
                value => value.Id == requirement.Id,
                cancellationToken);
        var persistedAttempt = await verification.RequirementProcessingAttempts
            .SingleAsync(
                value => value.Id == attempt.Id,
                cancellationToken);

        Assert.Equal(RequirementStatus.Failed, persistedRequirement.Status);
        Assert.Equal(
            DocumentProcessingState.Finished,
            persistedAttempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, persistedAttempt.Outcome);
        Assert.Equal(
            "REQUIREMENT_PERSISTENCE_ERROR",
            persistedAttempt.ErrorCode);
    }

    [Fact]
    public async Task SaveChanges_WithDatabaseError_ThrowsTypedException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.RequireAvailable();
        await using var context = fixture.CreateDbContext();
        var repository = new RequirementRepository(context);
        var requirement = Requirement.Create(Guid.NewGuid(), Guid.NewGuid(), RequirementCommercialLine.Essential, At);
        repository.Add(requirement);

        await Assert.ThrowsAsync<RequirementPersistenceException>(() =>
            repository.SaveChangesAsync(cancellationToken));
    }

    private async Task<SeededPreQuote> SeedPreQuoteAsync()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var user = User.CreateFromGoogle(
            "owner@example.com",
            "Owner",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Client",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            user.Id,
            At);
        var project = Project.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            "Bogota",
            user.Id,
            At);
        var preQuote = PreQuote.Create(project.Id, user.Id, "PC-2020-0001", null, At);
        context.Users.Add(user);
        context.Clients.Add(client);
        context.Projects.Add(project);
        context.PreQuotes.Add(preQuote);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededPreQuote(user.Id, preQuote.Id);
    }

    private sealed record SeededPreQuote(Guid UserId, Guid PreQuoteId);
}
