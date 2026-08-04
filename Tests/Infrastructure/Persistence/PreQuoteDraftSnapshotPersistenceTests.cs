using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PreQuoteDraftSnapshotPersistenceTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DraftSnapshots_RoundTripAcrossNewDbContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 1,
            cancellationToken);

        await using var context = fixture.CreateDbContext();
        var result = await new PreQuoteDraftRepository(context)
            .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(seeded.DraftId, result.Id);
        var item = Assert.Single(result.Items);

        Assert.NotNull(item.GlassSnapshot);
        var glass = item.GlassSnapshot;
        Assert.Equal("LAM_4_4", glass.NormalizedCodeSnapshot);
        Assert.Equal("Laminado 4+4", glass.RawSpecification);
        Assert.Equal(
            [GlassReviewReason.GlassTypeNotIdentified, GlassReviewReason.GlassTypeConflict],
            glass.ReviewReasons.Select(x => x.Code).OrderBy(x => x).ToArray());

        Assert.Equal([1, 2], glass.SourcePages
            .OrderBy(x => x.Sequence)
            .Select(x => x.PageNumber)
            .ToArray());

        var evidence = glass.Evidence.OrderBy(x => x.Sequence).ToArray();
        Assert.Single(evidence);
        Assert.Equal(EvidenceSourceType.Native, evidence[0].SourceType);
        Assert.Equal(2, evidence[0].PageNumber);
        Assert.Equal("Synthetic evidence", evidence[0].Text);

        Assert.NotNull(item.ValuationSnapshot);
        var valuation = item.ValuationSnapshot;
        Assert.Equal(PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Equal("COP", valuation.Currency);
        Assert.Equal(1.500000m, valuation.UnitAreaSquareMeters);
        Assert.Equal(seeded.ExpectedTotalAreaSquareMeters, valuation.TotalAreaSquareMeters);
        Assert.Equal(90000.123456m, valuation.UnitPricePerSquareMeter);
        Assert.Equal(seeded.ExpectedUnitAmount, valuation.UnitAmount);
        Assert.Equal(seeded.ExpectedTotalAmount, valuation.TotalAmount);
        Assert.Equal(
            At.AddMinutes(2),
            valuation.ValuedAtUtc);

        Assert.True(result.EconomicSummary.IsEconomicallyComplete);
        Assert.Equal(1, result.EconomicSummary.IncludedItemCount);
        Assert.Equal(3, result.EconomicSummary.IncludedKnownQuoteableUnitCount);
        Assert.Equal(1, result.EconomicSummary.ValuedItemCount);
        Assert.Equal(0, result.EconomicSummary.StaleValuationItemCount);
    }

    [Fact]
    public async Task DraftSnapshot_RemainsUnchangedAfterCatalogMutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 1,
            createCatalogMutationTarget: true,
            cancellationToken: cancellationToken);

        decimal? expectedTotalAreaBeforeCatalogMutation;
        await using (var snapshotContext = fixture.CreateDbContext())
        {
            var snapshot = await new PreQuoteDraftRepository(snapshotContext)
                .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);
            Assert.NotNull(snapshot);
            var snapshotItem = Assert.Single(snapshot.Items);
            Assert.NotNull(snapshotItem.ValuationSnapshot);

            expectedTotalAreaBeforeCatalogMutation = snapshotItem.ValuationSnapshot.TotalAreaSquareMeters;
        }

        await using (var mutationContext = fixture.CreateDbContext())
        {
            await mutationContext.GlassPriceRangeVersions
                .Where(version => version.Id == seeded.PriceRangeId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(version => version.MinimumPricePerSquareMeter,
                        1_500.000000m)
                    .SetProperty(version => version.MaximumPricePerSquareMeter,
                        2_000.000000m),
                    cancellationToken);

            await mutationContext.GlassTypes
                .Where(type => type.Id == seeded.GlassTypeId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(type => type.Code, "MOD"),
                    cancellationToken);
        }

        await using var context = fixture.CreateDbContext();
        var result = await new PreQuoteDraftRepository(context)
            .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);

            Assert.NotNull(result);
            var item = Assert.Single(result.Items);
            Assert.NotNull(item.GlassSnapshot);
            Assert.NotNull(item.ValuationSnapshot);
            var glass = item.GlassSnapshot;
            var valuation = item.ValuationSnapshot;

        Assert.Equal("LAM_4_4", glass.NormalizedCodeSnapshot);
        Assert.Equal("Laminado 4+4", glass.RawSpecification);
        Assert.Equal("COP", valuation.Currency);
        Assert.Equal(1.500000m, valuation.UnitAreaSquareMeters);
        Assert.Equal(expectedTotalAreaBeforeCatalogMutation, valuation.TotalAreaSquareMeters);
        Assert.Equal(90000.123456m, valuation.UnitPricePerSquareMeter);
        Assert.Equal(3, result.EconomicSummary.IncludedKnownQuoteableUnitCount);
        Assert.Equal(seeded.ExpectedTotalAmount, valuation.TotalAmount);
        Assert.Equal(seeded.ExpectedUnitAmount, valuation.UnitAmount);
    }

    [Fact]
    public async Task HistoricalDraftWithoutSnapshots_LoadsAsEconomicallyIncomplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedDraftAsync(
            includeSnapshots: false,
            includeValuation: false,
            itemCount: 1,
            cancellationToken);

        await using var context = fixture.CreateDbContext();
        var result = await new PreQuoteDraftRepository(context)
            .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);

        Assert.Null(item.GlassSnapshot);
        Assert.Null(item.ValuationSnapshot);
        Assert.Equal(1, result.EconomicSummary.PendingValuationItemCount);
        Assert.Equal(3, result.EconomicSummary.IncludedKnownQuoteableUnitCount);
        Assert.False(result.EconomicSummary.IsEconomicallyComplete);
        Assert.Equal(seeded.OwnerUserId, result.CreatedByUserId);
    }

    [Fact]
    public async Task FindForUpdate_LoadsTrackedSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 1,
            cancellationToken);

        await using (var updateContext = fixture.CreateDbContext())
        {
            var repository = new PreQuoteDraftRepository(updateContext);
            var draft = await repository.FindForUpdateAsync(
                seeded.PreQuoteId,
                seeded.OwnerUserId,
                cancellationToken);

            Assert.NotNull(draft);
            var item = Assert.Single(draft.Items);
            Assert.NotNull(item.ValuationSnapshot);
            var valuation = item.ValuationSnapshot;

            Assert.Equal(EntityState.Unchanged, updateContext.Entry(draft).State);
            Assert.Equal(EntityState.Unchanged, updateContext.Entry(item).State);
            Assert.Equal(EntityState.Unchanged,
                updateContext.Entry(valuation).State);

            draft!.Update(
                draft.Version,
                draft.ProjectName,
                draft.ClientName,
                draft.Location,
                [new(
                    item.Id,
                    item.Sequence,
                    item.Reference,
                    item.Description,
                    item.ElementType,
                    item.RawMeasurements,
                    1200,
                    item.HeightMillimeters,
                    item.Quantity,
                    item.IsIncluded)],
                ExistingRequirements(draft),
                ExistingReferences(draft),
                PendingIssues(draft),
                PendingConflicts(draft),
                seeded.OwnerUserId,
                At.AddMinutes(10));

            Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
            Assert.Equal(
                PreQuoteDraftValuationInvalidationReason.WidthChanged,
                valuation!.InvalidationReason);
            Assert.NotNull(valuation!.InvalidatedAtUtc);

            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await new PreQuoteDraftRepository(verifyContext)
            .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);

        Assert.NotNull(persisted);
        var persistedItem = Assert.Single(persisted.Items);

        Assert.Equal(PreQuoteDraftValuationStatus.Stale,
            persistedItem.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.WidthChanged,
            persistedItem.ValuationSnapshot?.InvalidationReason);
        Assert.Equal(At.AddMinutes(10), persistedItem.ValuationSnapshot?.InvalidatedAtUtc);
        Assert.NotNull(persistedItem.ValuationSnapshot);
        Assert.NotNull(persistedItem.ValuationSnapshot!.UnitAmount);
        Assert.NotNull(persistedItem.ValuationSnapshot.UnitPricePerSquareMeter);
        Assert.NotNull(persistedItem.ValuationSnapshot.TotalAreaSquareMeters);
        Assert.NotNull(persistedItem.ValuationSnapshot.TotalAmount);
        Assert.Equal(0, persisted.EconomicSummary.ValuedItemCount);
        Assert.Equal(1, persisted.EconomicSummary.StaleValuationItemCount);
        Assert.False(persisted.EconomicSummary.IsEconomicallyComplete);
    }

    [Fact]
    public async Task ForeignOwner_CannotLoadPersistedSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 1,
            cancellationToken);

        await using var context = fixture.CreateDbContext();
        var wrongOwner = new Guid("6f9e7f3a-1c4c-4f7f-9d9e-1bb8ec7d0e67");

        var wrong = await new PreQuoteDraftRepository(context)
            .FindReadAsync(seeded.PreQuoteId, wrongOwner, cancellationToken);

        Assert.Null(wrong);

        var correct = await new PreQuoteDraftRepository(context)
            .FindReadAsync(seeded.PreQuoteId, seeded.OwnerUserId, cancellationToken);

        Assert.NotNull(correct);
        var item = Assert.Single(correct.Items);
        Assert.NotNull(item.GlassSnapshot);
        Assert.NotNull(item.ValuationSnapshot);
    }

    [Fact]
    public async Task FindReadAsync_DoesNotQueryExtractionTablesOrIntroduceNPlusOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var oneItem = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 1,
            cancellationToken);
        var oneItemCommands = await MeasureSelectCommandsAsync(
            oneItem.PreQuoteId,
            oneItem.OwnerUserId,
            cancellationToken);

        var threeItems = await SeedDraftAsync(
            includeSnapshots: true,
            includeValuation: true,
            itemCount: 3,
            cancellationToken);
        var threeItemCommands = await MeasureSelectCommandsAsync(
            threeItems.PreQuoteId,
            threeItems.OwnerUserId,
            cancellationToken);

        Assert.All(oneItemCommands.SelectCommands,
            command => Assert.False(ContainsStructuredExtractionTables(command)));
        Assert.All(threeItemCommands.SelectCommands,
            command => Assert.False(ContainsStructuredExtractionTables(command)));

        Assert.Equal(oneItemCommands.SelectCommandCount,
            threeItemCommands.SelectCommandCount);
        Assert.True(oneItemCommands.SelectCommandCount > 0);
    }

    private async Task<MeasureResult> MeasureSelectCommandsAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var interceptor = new QueryCommandCaptureInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var result = await new PreQuoteDraftRepository(context)
            .FindReadAsync(preQuoteId, ownerUserId, cancellationToken);

        Assert.NotNull(result);

        var commands = interceptor.ExecutedCommands
            .Where(IsSelectCommand)
            .ToArray();

        return new MeasureResult(commands.Length, commands);
    }

    private static bool IsSelectCommand(string commandText)
    {
        var trimmed = commandText.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsStructuredExtractionTables(string commandText)
    {
        return commandText.Contains("structured_document_extractions",
                    StringComparison.OrdinalIgnoreCase)
            || commandText.Contains("structured_extraction_items",
                StringComparison.OrdinalIgnoreCase)
            || commandText.Contains("structured_item_glass_valuations",
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SeededDraft> SeedDraftAsync(
        bool includeSnapshots,
        bool includeValuation,
        int itemCount,
        CancellationToken cancellationToken,
        bool createCatalogMutationTarget = false)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();

        await using var context = fixture.CreateDbContext();

        var owner = User.CreateFromGoogle(
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
            "Bogota",
            owner.Id,
            At);
        var project = Project.Create(
            client.Id,
            "PRJ-1",
            "Synthetic project",
            null,
            "Bogota",
            owner.Id,
            At);
        var preQuote = PreQuote.Create(project.Id, owner.Id, At);
        var document = PreQuoteDocument.Create(
            preQuote.Id,
            "document.pdf",
            "application/pdf",
            1,
            "prequotes/document.pdf",
            owner.Id,
            At);
        var attempt = DocumentProcessingAttempt.Create(
            document.Id,
            owner.Id,
            Guid.NewGuid(),
            At);
        attempt.Start(At.AddMinutes(1));
        attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(2));

        var result = DocumentExtractionResult.Create(
            attempt.Id,
            "3.0",
            PdfClassification.PdfText,
            false,
            1,
            "pymupdf",
            5,
            "{\"a\":1}",
            At.AddMinutes(2));

        Guid? glassTypeId = null;
        Guid? priceRangeId = null;
        if (includeSnapshots)
        {
            var glassType = GlassType.Create(
                "LAM_4_4",
                "Laminated 4+4",
                null,
                At);
            var priceRange = GlassPriceRangeVersion.Create(
                glassType.Id,
                1,
                90000.123456m,
                110000.123456m,
                "COP",
                GlassPriceRangeStatus.Active,
                At,
                null,
                At);

            glassTypeId = glassType.Id;
            priceRangeId = priceRange.Id;
            context.AddRange(glassType, priceRange);

            if (createCatalogMutationTarget)
            {
                context.Add(GlassPriceRangeVersion.Create(
                    glassType.Id,
                    2,
                    120000.123456m,
                    130000.123456m,
                    "COP",
                    GlassPriceRangeStatus.Active,
                    At.AddMonths(1),
                    At.AddMonths(2),
                    At.AddMonths(1)));
            }
        }

        var extractionItems = Enumerable.Range(1, itemCount)
            .Select(index =>
            {
                var requiresReview = includeSnapshots && index == 1;
                GlassReviewReason[] reviewReasons = requiresReview
                    ? [GlassReviewReason.GlassTypeNotIdentified, GlassReviewReason.GlassTypeConflict]
                    : [];
                int[] sourcePages = requiresReview
                    ? [1, 2]
                    : [1];
                StructuredItemGlassEvidenceInput[] evidence = requiresReview
                    ? [new(1, 1, EvidenceSourceType.Native, "Synthetic evidence"),
                       new(2, 2, EvidenceSourceType.Native, "Synthetic evidence")]
                    : [new(1, 1, EvidenceSourceType.Native, "Synthetic evidence")];
                StructuredItemGlassInput? glass = includeSnapshots
                    ? new StructuredItemGlassInput(
                        glassTypeId,
                        "Laminado 4+4",
                        "LAM_4_4",
                        GlassAssignmentScope.Item,
                        requiresReview,
                        reviewReasons,
                        sourcePages,
                        evidence)
                    : null;
                var valuation = includeSnapshots && includeValuation
                    && glassTypeId is { } valuationGlassTypeId
                    && priceRangeId is { } valuationPriceRangeId
                    ? new StructuredItemGlassValuationInput(
                        GlassValuationStatus.Valued,
                        null,
                        valuationGlassTypeId,
                        valuationPriceRangeId,
                        1,
                        GlassPriceRangeStatus.Preliminary,
                        "COP",
                        1.500000m,
                        3.000000m,
                        90000.123456m,
                        110000.123456m,
                        270000.370368m,
                        810001.111104m)
                    : null;

                return new StructuredItemInput(
                    index,
                    $"W-{index}",
                    "Synthetic window",
                    StructuredElementType.Window,
                    "1000 x 1500 mm",
                    1000,
                    1500,
                    3,
                    requiresReview,
                    glass,
                    valuation);
            })
            .ToArray();

        var extraction = StructuredDocumentExtraction.Create(
            result.Id,
            StructuredExtractionStatus.Completed,
            "Synthetic project",
            "Synthetic client",
            "Bogota",
            itemCount,
            0,
            extractionItems.Count(x => x.RequiresReview),
            itemCount * 3,
            "rule_based_v2",
            15,
            extractionItems,
            [],
            [],
            [],
            [],
            At.AddMinutes(2),
            includeSnapshots ? itemCount : null,
            includeSnapshots ? (extractionItems.Any(x => x.Glass?.RequiresReview is true) ? 1 : 0) : null);

        var draftedItems = extraction.Items
            .OrderBy(value => value.Sequence)
            .Select(value =>
            {
                PreQuoteDraftItemGlassSnapshotSource? glass = null;
                if (includeSnapshots)
                {
                    glass = new PreQuoteDraftItemGlassSnapshotSource(
                        value.Id,
                        glassTypeId,
                        "Laminado 4+4",
                        "LAM_4_4",
                        GlassAssignmentScope.Item,
                        value.GlassDetection?.RequiresReview is true,
                        value.GlassDetection?.ReviewReasons
                            .Select(reason => reason.Code)
                            .ToArray() ?? [],
                        [1, 2],
                        [new(
                            1,
                            2,
                            EvidenceSourceType.Native,
                            "Synthetic evidence")]);
                }

                PreQuoteDraftItemValuationSnapshotSource? valuation = null;
                if (includeSnapshots && includeValuation
                    && value.GlassValuation is not null
                    && glassTypeId is { } snapshotGlassTypeId
                    && priceRangeId is { } snapshotPriceRangeId)
                {
                    valuation = new PreQuoteDraftItemValuationSnapshotSource(
                        value.GlassValuation.Id,
                        PreQuoteDraftValuationStatus.Valued,
                        null,
                        snapshotGlassTypeId,
                        snapshotPriceRangeId,
                        1000,
                        1500,
                        3,
                        1.500000m,
                        4.500000m,
                        90000.123456m,
                        270000.370368m,
                        810001.111104m,
                        "COP",
                        At.AddMinutes(2),
                        null,
                        null);
                }

                return new PreQuoteDraftItemSource(
                    value.Id,
                    value.Sequence,
                    value.Reference,
                    value.Description,
                    value.ElementType,
                    value.RawMeasurements,
                    value.WidthMillimeters,
                    value.HeightMillimeters,
                    value.Quantity,
                    glass,
                    valuation);
            })
            .ToArray();
        var items = draftedItems
            .ToArray();

        var draft = PreQuoteDraft.Create(
            preQuote.Id,
            document.Id,
            extraction.Id,
            "Synthetic project",
            "Synthetic client",
            "Bogota",
            owner.Id,
            At,
            items,
            [],
            [],
            [],
            []);

        context.AddRange(owner, client, project, preQuote, document, attempt,
            result, extraction, draft);
        await context.SaveChangesAsync(cancellationToken);

        var firstDraftedItem = items.FirstOrDefault();

        return new SeededDraft(
            owner.Id,
            Guid.NewGuid(),
            preQuote.Id,
            draft.Id,
            glassTypeId,
            priceRangeId,
            itemCount,
            firstDraftedItem?.Valuation?.TotalAreaSquareMeters,
            firstDraftedItem?.Valuation?.UnitAreaSquareMeters,
            firstDraftedItem?.Valuation?.UnitAmount,
            firstDraftedItem?.Valuation?.TotalAmount,
            firstDraftedItem?.Valuation?.Currency);
    }

    private ApplicationDbContext CreateContextWithInterceptor(
        QueryCommandCaptureInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class QueryCommandCaptureInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> ExecutedCommands => _commands;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private static PreQuoteDraftRequirementEdit[] ExistingRequirements(
        PreQuoteDraft draft) =>
        draft.Requirements
            .OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftRequirementEdit(
                x.Id,
                x.Sequence,
                x.Category,
                x.Value,
                x.IsIncluded))
            .ToArray();

    private static PreQuoteDraftReferenceEdit[] ExistingReferences(
        PreQuoteDraft draft) =>
        draft.DocumentReferences
            .OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftReferenceEdit(
                x.Id,
                x.Sequence,
                x.Reference,
                x.Description,
                x.Detail,
                x.Quantity,
                x.IsIncluded))
            .ToArray();

    private static PreQuoteDraftResolutionEdit[] PendingIssues(
        PreQuoteDraft draft) =>
        draft.Issues
            .OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftResolutionEdit(
                x.Id,
                PreQuoteDraftResolutionStatus.Pending,
                null))
            .ToArray();

    private static PreQuoteDraftResolutionEdit[] PendingConflicts(
        PreQuoteDraft draft) =>
        draft.Conflicts
            .OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftResolutionEdit(
                x.Id,
                PreQuoteDraftResolutionStatus.Pending,
                null))
            .ToArray();

    private sealed record SeededDraft(
        Guid OwnerUserId,
        Guid ForeignUserId,
        Guid PreQuoteId,
        Guid DraftId,
        Guid? GlassTypeId,
        Guid? PriceRangeId,
        int ItemCount,
        decimal? ExpectedTotalAreaSquareMeters,
        decimal? ExpectedUnitAreaSquareMeters,
        decimal? ExpectedUnitAmount,
        decimal? ExpectedTotalAmount,
        string? ExpectedCurrency);

    private sealed record MeasureResult(
        int SelectCommandCount,
        string[] SelectCommands);
}
