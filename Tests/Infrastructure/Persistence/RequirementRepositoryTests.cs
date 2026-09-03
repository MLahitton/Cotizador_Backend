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
    public async Task PricingSnapshotUpdate_WithConcurrentTransactions_SerializesGrandTotalRecalculation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPricedRequirementAsync();
        var firstLocked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstReprice = RepricePersistedItemAsync(
            seeded.RequirementId,
            seeded.FirstProposalItemId,
            50m,
            firstLocked,
            releaseFirst.Task,
            cancellationToken);

        await firstLocked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        var secondReprice = RepricePersistedItemAsync(
            seeded.RequirementId,
            seeded.SecondProposalItemId,
            80m,
            null,
            Task.CompletedTask,
            cancellationToken);

        await Task.Delay(200, cancellationToken);
        Assert.False(secondReprice.IsCompleted);

        releaseFirst.SetResult();
        await Task.WhenAll(firstReprice, secondReprice)
            .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);

        await using var verification = fixture.CreateDbContext();
        var snapshot = await verification.RequirementPricingSnapshots
            .AsNoTracking()
            .Include(value => value.Items)
            .SingleAsync(
                value => value.RequirementId == seeded.RequirementId,
                cancellationToken);
        var firstItem = snapshot.Items.Single(
            item => item.TechnicalProposalItemId == seeded.FirstProposalItemId);
        var secondItem = snapshot.Items.Single(
            item => item.TechnicalProposalItemId == seeded.SecondProposalItemId);

        Assert.Equal(300m, snapshot.OriginalGrandTotal);
        Assert.Equal(130m, snapshot.CurrentGrandTotal);
        Assert.Equal(-170m, snapshot.DeltaGrandTotal);
        Assert.Equal(50m, firstItem.CurrentLineExpected);
        Assert.Equal(80m, secondItem.CurrentLineExpected);
        Assert.Equal(
            snapshot.CurrentGrandTotal,
            snapshot.Items.Sum(item => item.CurrentLineExpected));
        Assert.Equal(
            snapshot.DeltaGrandTotal,
            snapshot.Items.Sum(item => item.DeltaLineExpected));
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

    [Fact]
    public async Task ReplacePricingSnapshot_WithNewTechnicalProposal_ReusesCurrentRequirementSnapshotRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPricedRequirementAsync();

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new RequirementRepository(context);
            var attempt = RequirementProcessingAttempt.Create(
                seeded.RequirementId,
                seeded.UserId,
                Guid.NewGuid(),
                At.AddMinutes(10));
            attempt.Start(At.AddMinutes(11));
            attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(12));
            var extraction = RequirementExtractionResult.Create(
                attempt.Id,
                "3.0",
                "AI2",
                "{}",
                1,
                0,
                0,
                0,
                "test",
                100,
                At.AddMinutes(13));
            var extracted = CreateExtractedItem(extraction.Id, 3, "C");
            var proposal = RequirementTechnicalProposal.Create(
                seeded.RequirementId,
                extraction.Id,
                attempt.Id,
                false,
                At.AddMinutes(14));
            var proposalItem = CreateProposalItem(proposal.Id, extracted.Id);
            proposal.AddItem(proposalItem);
            context.RequirementProcessingAttempts.Add(attempt);
            context.RequirementExtractionResults.Add(extraction);
            context.RequirementExtractedItems.Add(extracted);
            context.RequirementTechnicalProposals.Add(proposal);
            await context.SaveChangesAsync(cancellationToken);

            var current = await repository.FindCurrentPricingSnapshotAsync(
                seeded.RequirementId,
                cancellationToken);
            Assert.NotNull(current);
            var currentSnapshotId = current!.Id;
            var replacement = RequirementPricingSnapshot.Create(
                seeded.RequirementId,
                proposal.Id,
                proposal.CommercialRevision,
                "COP",
                "HISTORICAL_COMPARABLES",
                500m,
                500m,
                At.AddMinutes(15));
            replacement.AddItem(CreatePricingItem(
                replacement.Id,
                proposalItem.Id,
                500m));

            repository.ReplacePricingSnapshot(current, replacement);
            await repository.SaveChangesAsync(cancellationToken);

            Assert.Equal(currentSnapshotId, current.Id);
        }

        await using var verification = fixture.CreateDbContext();
        var snapshots = await verification.RequirementPricingSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Items)
            .Where(snapshot => snapshot.RequirementId == seeded.RequirementId)
            .ToArrayAsync(cancellationToken);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(500m, snapshot.CurrentGrandTotal);
        Assert.Equal(1, snapshot.TechnicalProposalCommercialRevision);
        var item = Assert.Single(snapshot.Items);
        Assert.NotEqual(seeded.FirstProposalItemId, item.TechnicalProposalItemId);
        Assert.NotEqual(seeded.SecondProposalItemId, item.TechnicalProposalItemId);
        Assert.Equal(500m, item.CurrentLineExpected);
    }

    private async Task<SeededPricedRequirement> SeedPricedRequirementAsync()
    {
        var seeded = await SeedPreQuoteAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var requirement = Requirement.Create(
            seeded.PreQuoteId,
            seeded.UserId,
            RequirementCommercialLine.Essential,
            At.AddMinutes(1));
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(2));
        attempt.Start(At.AddMinutes(3));
        attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(4));
        var extraction = RequirementExtractionResult.Create(
            attempt.Id,
            "3.0",
            "AI2",
            "{}",
            2,
            0,
            0,
            0,
            "test",
            100,
            At.AddMinutes(5));
        var firstExtracted = CreateExtractedItem(extraction.Id, 1, "A");
        var secondExtracted = CreateExtractedItem(extraction.Id, 2, "B");
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            attempt.Id,
            false,
            At.AddMinutes(6));
        var firstProposalItem = CreateProposalItem(proposal.Id, firstExtracted.Id);
        var secondProposalItem = CreateProposalItem(proposal.Id, secondExtracted.Id);
        proposal.AddItem(firstProposalItem);
        proposal.AddItem(secondProposalItem);
        var snapshot = RequirementPricingSnapshot.Create(
            requirement.Id,
            proposal.Id,
            proposal.CommercialRevision,
            "COP",
            "HISTORICAL_COMPARABLES",
            300m,
            300m,
            At.AddMinutes(7));
        snapshot.AddItem(CreatePricingItem(snapshot.Id, firstProposalItem.Id, 100m));
        snapshot.AddItem(CreatePricingItem(snapshot.Id, secondProposalItem.Id, 200m));

        await using var context = fixture.CreateDbContext();
        context.Requirements.Add(requirement);
        context.RequirementProcessingAttempts.Add(attempt);
        context.RequirementExtractionResults.Add(extraction);
        context.RequirementExtractedItems.AddRange(firstExtracted, secondExtracted);
        context.RequirementTechnicalProposals.Add(proposal);
        context.RequirementPricingSnapshots.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);
        return new SeededPricedRequirement(
            seeded.UserId,
            requirement.Id,
            firstProposalItem.Id,
            secondProposalItem.Id);
    }

    private async Task RepricePersistedItemAsync(
        Guid requirementId,
        Guid proposalItemId,
        decimal lineExpected,
        TaskCompletionSource? locked,
        Task release,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        var repository = new RequirementRepository(context);
        await using var transaction =
            await repository.BeginPricingUpdateTransactionAsync(cancellationToken);
        await repository.FindCurrentTechnicalProposalForUpdateAsync(
            requirementId,
            cancellationToken);
        var snapshot = await repository.FindCurrentPricingSnapshotForUpdateAsync(
            requirementId,
            cancellationToken);

        locked?.SetResult();
        await release.WaitAsync(cancellationToken);

        var item = Assert.Single(
            snapshot!.Items,
            value => value.TechnicalProposalItemId == proposalItemId);
        item.UpdateCurrent(
            item.CurrentSystemId,
            item.CurrentGlassTypeId,
            item.CurrentFinishTypeId,
            item.CurrentStatus,
            lineExpected,
            lineExpected,
            lineExpected,
            lineExpected,
            lineExpected,
            lineExpected,
            At.AddMinutes(8));
        snapshot.RecalculateCurrentGrandTotal(At.AddMinutes(9));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static RequirementExtractedItem CreateExtractedItem(
        Guid extractionId,
        int sequence,
        string reference) =>
        RequirementExtractedItem.Create(
            extractionId,
            $"ai2-{sequence}",
            sequence,
            reference,
            $"Item {sequence}",
            StructuredElementType.Window,
            1,
            1000,
            1000,
            1m,
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "WINDOW",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            At.AddMinutes(5));

    private static RequirementTechnicalProposalItem CreateProposalItem(
        Guid proposalId,
        Guid extractedItemId) =>
        RequirementTechnicalProposalItem.Create(
            proposalId,
            extractedItemId,
            null,
            null,
            null,
            0.9m,
            0.9m,
            0.9m,
            0.9m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "NO_HISTORY",
            At.AddMinutes(6));

    private static RequirementPricingItemSnapshot CreatePricingItem(
        Guid snapshotId,
        Guid proposalItemId,
        decimal expected) =>
        RequirementPricingItemSnapshot.Create(
            snapshotId,
            proposalItemId,
            null,
            null,
            null,
            "PRICEABLE",
            expected,
            expected,
            expected,
            expected,
            expected,
            expected,
            At.AddMinutes(7));

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

    private sealed record SeededPricedRequirement(
        Guid UserId,
        Guid RequirementId,
        Guid FirstProposalItemId,
        Guid SecondProposalItemId);
}
