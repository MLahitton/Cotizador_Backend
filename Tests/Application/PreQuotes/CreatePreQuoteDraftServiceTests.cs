using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.CreatePreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreatePreQuoteDraftServiceTests
{
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid ExtractionId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_FromSchema3Completed_CopiesGlassAndValuation()
    {
        var source = CreateSourceContext(
            includeGlass: true,
            includeValuation: true,
            valuationUnitArea: 1.500000m,
            valuationTotalArea: 4.500000m,
            valuationUnitPrice: 90000.123456m,
            valuationUnitAmount: 270000.370368m,
            valuationTotalAmount: 810001.111104m);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var item = Assert.Single(draft.Items);

        var glass = item.GlassSnapshot;
        Assert.NotNull(glass);
        Assert.Equal(glassTypeId, glass.GlassTypeId);
        Assert.Equal("LAM_4_4", glass.NormalizedCodeSnapshot);
        Assert.Equal("Laminado 4+4", glass.RawSpecification);

        var valuation = item.ValuationSnapshot;
        Assert.NotNull(valuation);
        Assert.Equal(PreQuoteDraftValuationStatus.Valued, valuation.Status);
        Assert.Equal(1.500000m, valuation.UnitAreaSquareMeters);
        Assert.Equal(4.500000m, valuation.TotalAreaSquareMeters);
        Assert.Equal(90000.123456m, valuation.UnitPricePerSquareMeter);
        Assert.Equal(270000.370368m, valuation.UnitAmount);
        Assert.Equal(810001.111104m, valuation.TotalAmount);
    }

    [Fact]
    public async Task Create_FromSchema3RequiresReview_CopiesReviewMetadata()
    {
        var source = CreateSourceContext(
            includeGlass: true,
            includeValuation: false,
            glassRequiresReview: true,
            glassReviewReasons: [GlassReviewReason.GlassTypeNotIdentified,
                GlassReviewReason.GlassTypeConflict],
            glassSourcePages: [1, 2],
            glassEvidence:
            [
                new(1, 1, EvidenceSourceType.Native, "Evidence #1"),
                new(2, 2, EvidenceSourceType.Native, "Evidence #2")
            ]);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var item = Assert.Single(draft.Items);
        Assert.Equal(PreQuoteDraftValuationStatus.RequiresReview, item.ValuationStatus);

        var glass = item.GlassSnapshot;
        Assert.NotNull(glass);
        Assert.True(glass.RequiresReview);
        Assert.Equal(
            [GlassReviewReason.GlassTypeNotIdentified,
                GlassReviewReason.GlassTypeConflict],
            glass.ReviewReasons
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToArray());
        Assert.Equal([1, 2], glass.SourcePages
            .OrderBy(x => x.Sequence)
            .Select(x => x.PageNumber)
            .ToArray());

        var evidence = glass.Evidence.OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Equal(1, evidence[0].PageNumber);
        Assert.Equal(1, evidence[0].Sequence);
        Assert.Equal("Evidence #1", evidence[0].Text);
        Assert.Equal(2, evidence[1].Sequence);
        Assert.Equal(2, evidence[1].PageNumber);
        Assert.Equal("Evidence #2", evidence[1].Text);
    }

    [Fact]
    public async Task Create_FromSchema3WithoutGlass_CreatesItemWithoutGlassSnapshot()
    {
        var source = CreateSourceContext(includeGlass: false, includeValuation: true);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var item = Assert.Single(draft.Items);
        Assert.Null(item.GlassSnapshot);
        Assert.NotNull(item.ValuationSnapshot);
    }

    [Fact]
    public async Task Create_FromSchema3WithoutValuation_PreservesPendingState()
    {
        var source = CreateSourceContext(includeGlass: true, includeValuation: false);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var item = Assert.Single(draft.Items);
        Assert.NotNull(item.GlassSnapshot);
        Assert.Null(item.ValuationSnapshot);
        Assert.Equal(PreQuoteDraftValuationStatus.Pending, item.ValuationStatus);
    }

    [Fact]
    public async Task Create_FromSchema3MultipleItems_CopiesEachSnapshot()
    {
        var source = CreateSourceContext(
            itemCount: 2,
            includeGlass: true,
            includeValuation: true);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var items = draft.Items.OrderBy(x => x.Sequence).ToArray();

        Assert.Equal(2, items.Length);
        Assert.Equal(2, items.Count(x => x.GlassSnapshot is not null));
        Assert.Equal(2, items.Count(x => x.ValuationSnapshot is not null));
        Assert.Equal([1, 2], items.Select(x => x.SourceItemSequence).ToArray());
    }

    [Fact]
    public async Task Create_FromSchema3_PreservesDecimalPrecision()
    {
        var source = CreateSourceContext(
            includeGlass: true,
            includeValuation: true,
            glassSourcePages: [1, 2],
            valuationUnitArea: 1.123456m,
            valuationTotalArea: 2.654321m,
            valuationUnitAmount: 12345.123456m,
            valuationTotalAmount: 67890.987654m,
            valuationUnitPrice: 44444.444444m);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var valuation = Assert.Single(draft.Items).ValuationSnapshot;
        Assert.NotNull(valuation);
        Assert.Equal(1.123456m, valuation.UnitAreaSquareMeters);
        Assert.Equal(2.654321m, valuation.TotalAreaSquareMeters);
        Assert.Equal(12345.123456m, valuation.UnitAmount);
        Assert.Equal(67890.987654m, valuation.TotalAmount);
        Assert.Equal(44444.444444m, valuation.UnitPricePerSquareMeter);
    }

    [Fact]
    public async Task Create_WithFixedFunctionalType_WritesSuggestedSystemWithoutSelected()
    {
        var source = CreateSourceContext(
            includeGlass: true,
            includeValuation: false,
            includeTechnical: true,
            requestedSystemCode: "3831",
            requestedSystemOriginalText: "Sistema solicitado 3831",
            functionalType: "FIXED");
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId, DocumentId, ExtractionId, Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(source);
        repository.ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new DeterministicSgTechnicalSelector(
                new ProductSystemCatalog([System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL")])),
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        var selection = Assert.Single(result.Draft!.Items).TechnicalSelection;
        Assert.NotNull(selection);
        Assert.Equal("3831", selection.RequestedSystemCode);
        Assert.Equal("K40", selection.SuggestedSystemCode);
        Assert.Null(selection.SelectedSystemCode);
        Assert.Equal(
            PreQuoteDraftTechnicalSelectionState.Suggested,
            selection.SelectionState);
        Assert.Equal(
            PreQuoteDraftTechnicalSelectionSource.Rule,
            selection.SuggestedSource);
        Assert.Equal(
            SgTechnicalSelectionRuleCodes.SystemFixedFermo,
            selection.AppliedSystemRuleCode);
    }

    [Fact]
    public async Task Create_WithExistingSelectedTechnicalSelection_DoesNotOverwriteSelected()
    {
        var existing = new PreQuoteDraftItemTechnicalSelectionSource(
            RequestedSystemCode: "3831",
            RequestedSystemOriginalText: "Sistema solicitado 3831",
            SelectedSystemCode: "MANUAL_SYSTEM",
            SelectionState: PreQuoteDraftTechnicalSelectionState.Modified,
            RequiresReview: false,
            ReviewReasons: [],
            SelectedSource: PreQuoteDraftTechnicalSelectionSource.Manual);
        var source = CreateSourceContext(
            includeGlass: true,
            includeValuation: false,
            includeTechnical: true,
            requestedSystemCode: "3831",
            requestedSystemOriginalText: "Sistema solicitado 3831",
            functionalType: "FIXED",
            technicalSelection: existing);
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId, DocumentId, ExtractionId, Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(source);
        repository.ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new DeterministicSgTechnicalSelector(
                new ProductSystemCatalog([System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL")])),
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        var selection = Assert.Single(result.Draft!.Items).TechnicalSelection;
        Assert.NotNull(selection);
        Assert.Equal("MANUAL_SYSTEM", selection.SelectedSystemCode);
        Assert.Null(selection.SuggestedSystemCode);
        Assert.Equal(
            PreQuoteDraftTechnicalSelectionSource.Manual,
            selection.SelectedSource);
    }

    [Fact]
    public async Task Create_FromSchema2_RemainsCompatible()
    {
        var source = CreateSourceContext(
            schemaVersion: "2.0",
            includeGlass: false,
            includeValuation: false);

        var result = await ExecuteSuccessAsync(source);

        var draft = result.Draft;
        Assert.NotNull(draft);
        var item = Assert.Single(draft.Items);
        Assert.Null(item.GlassSnapshot);
        Assert.Null(item.ValuationSnapshot);
        Assert.Equal(PreQuoteDraftValuationStatus.Pending, item.ValuationStatus);
    }

    [Fact]
    public async Task Create_WithUnsupportedSchema_ReturnsContractualError()
    {
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
                PreQuoteId,
                DocumentId,
                ExtractionId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns((PreQuoteDraftSourceContext?)null);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        await repository.Received(1).FindSourceAsync(
            PreQuoteId,
            DocumentId,
            ExtractionId,
            Arg.Any<Guid>(),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Create_WithForeignOwner_ReturnsNotFound()
    {
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
                PreQuoteId,
                DocumentId,
                ExtractionId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns((PreQuoteDraftSourceContext?)null);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        await repository.Received(1).FindSourceAsync(
            PreQuoteId,
            DocumentId,
            ExtractionId,
            Arg.Any<Guid>(),
            TestContext.Current.CancellationToken);
        await repository.DidNotReceive().ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UsesSingleSaveChanges()
    {
        var source = CreateSourceContext(includeGlass: false, includeValuation: false);
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId, DocumentId, ExtractionId, Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(source);
        repository.ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenRepositoryQueryFails_ReturnsQueryError()
    {
        var source = CreateSourceContext(includeGlass: false, includeValuation: false);
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId, DocumentId, ExtractionId, Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromException<PreQuoteDraftSourceContext?>(
                new PreQuoteDraftQueryException(new InvalidOperationException("query"))));

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.QueryError, result.Failure);
    }

    [Fact]
    public async Task Create_WhenSaveFails_ReturnsPersistenceError()
    {
        var source = CreateSourceContext(includeGlass: false, includeValuation: false);
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId, DocumentId, ExtractionId, Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(source);
        repository.ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new PreQuoteDraftPersistenceException(new InvalidOperationException("save"))));

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.PersistenceError, result.Failure);
    }

    private static async Task<CreatePreQuoteDraftResult> ExecuteSuccessAsync(
        PreQuoteDraftSourceContext source)
    {
        var repository = CreateRepository();
        var currentUser = CreateCurrentUser();
        var identity = CreateIdentity(CreateUser());

        repository.FindSourceAsync(
            PreQuoteId,
            DocumentId,
            ExtractionId,
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(source);
        repository.ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Draft);
        Assert.Equal(PreQuoteId, result.Draft!.PreQuoteId);
        return result;
    }

    private static IPreQuoteDraftRepository CreateRepository() =>
        Substitute.For<IPreQuoteDraftRepository>();

    private static ICurrentUser CreateCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }

    private static IIdentityRepository CreateIdentity(User? user)
    {
        var identity = Substitute.For<IIdentityRepository>();
        identity.FindUserByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(user));
        return identity;
    }

    private static PreQuoteDraftSourceContext CreateSourceContext(
        string schemaVersion = "3.0",
        int itemCount = 1,
        bool includeGlass = true,
        bool includeValuation = true,
        bool glassRequiresReview = false,
        GlassReviewReason[]? glassReviewReasons = null,
        int[]? glassSourcePages = null,
        PreQuoteDraftItemGlassEvidenceSource[]? glassEvidence = null,
        decimal valuationUnitArea = 1.500000m,
        decimal valuationTotalArea = 4.500000m,
        decimal valuationUnitPrice = 90000.123456m,
        decimal valuationUnitAmount = 270000.370368m,
        decimal valuationTotalAmount = 810001.111104m,
        bool includeTechnical = false,
        string? requestedSystemCode = null,
        string? requestedSystemOriginalText = null,
        string? functionalType = null,
        PreQuoteDraftItemTechnicalSelectionSource? technicalSelection = null)
    {
        var reviewReasons = glassReviewReasons ?? Array.Empty<GlassReviewReason>();
        var sourcePages = glassSourcePages?.ToArray() ?? (glassRequiresReview ? new[] { 1, 2, 3 } : new[] { 1 });
        var evidence = glassEvidence
            ?? new[]
            {
                new PreQuoteDraftItemGlassEvidenceSource(
                    1,
                    1,
                    EvidenceSourceType.Native,
                    "Synthetic evidence")
            };

        Guid? glassType = includeGlass ? glassTypeId : null;
        GlassValuationReason? valuationReason = includeValuation
            ? GlassValuationReason.MissingQuantity
            : null;

        var items = Enumerable.Range(1, itemCount)
            .Select(index => new PreQuoteDraftItemSource(
                Guid.NewGuid(),
                index,
                $"W-{index:00}",
                "Synthetic window",
                StructuredElementType.Window,
                "1000 x 1500 mm",
                1000,
                1500,
                3,
                includeGlass
                    ? new PreQuoteDraftItemGlassSnapshotSource(
                        Guid.NewGuid(),
                        glassType,
                        "Laminado 4+4",
                        "LAM_4_4",
                        GlassAssignmentScope.Item,
                        index == 1 && glassRequiresReview,
                        reviewReasons,
                        sourcePages,
                        evidence)
                    : null,
                includeValuation
                    ? new PreQuoteDraftItemValuationSnapshotSource(
                        Guid.NewGuid(),
                        index == 1 && glassRequiresReview
                            ? PreQuoteDraftValuationStatus.RequiresReview
                            : PreQuoteDraftValuationStatus.Valued,
                        valuationReason,
                        glassType,
                        includeValuation ? Guid.NewGuid() : null,
                        1000,
                        1500,
                        3,
                        valuationUnitArea,
                        valuationTotalArea,
                        valuationUnitPrice,
                        valuationUnitAmount,
                        valuationTotalAmount,
                        "COP",
                        At,
                        null,
                        null)
                    : null,
                includeTechnical
                    ? new PreQuoteDraftItemTechnicalSnapshotSource(
                        Guid.NewGuid(),
                        requestedSystemCode,
                        requestedSystemOriginalText,
                        TechnicalClassificationSource.Explicit,
                        0.95m,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false,
                        [])
                    : null,
                technicalSelection,
                null,
                null,
                functionalType))
            .ToArray();

        return new(
            PreQuoteId,
            DocumentId,
            ExtractionId,
            true,
            true,
            "Synthetic project",
            "Synthetic client",
            "Bogota",
            items,
            Array.Empty<PreQuoteDraftRequirementSource>(),
            Array.Empty<PreQuoteDraftReferenceSource>(),
            Array.Empty<PreQuoteDraftIssueSource>(),
            Array.Empty<PreQuoteDraftConflictSource>());
    }

    private static User CreateUser() =>
        User.CreateFromGoogle(
            "owner@example.com",
            "Owner",
            "User",
            null,
            At);

    private static readonly Guid glassTypeId = Guid.NewGuid();

    private static ProductSystemCatalogReadModel System(
        string code,
        string functionalType,
        string? family,
        string? variant,
        string commercialLine) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            code,
            code,
            functionalType,
            family,
            null,
            commercialLine,
            variant,
            true,
            true,
            true,
            true,
            false,
            true);

    private sealed class ProductSystemCatalog(
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
        : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveSelectableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(systems.SingleOrDefault(system =>
                system.Code == code));
    }

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
