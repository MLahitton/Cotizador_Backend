using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.CreatePreQuoteDraft;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class CreatePreQuoteDraftServiceIntegrationTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateDraft_FromSchema3_PersistsCopiedSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.RequireAvailable();
        await fixture.ResetAsync();

        Guid ownerUserId;
        Guid preQuoteId;
        Guid documentId;
        Guid extractionId;
        Guid glassTypeId;
        Guid priceRangeId;
        User? owner = null;

        await using (var context = fixture.CreateDbContext())
        {
            owner = User.CreateFromGoogle(
                "owner@example.com",
                "Owner",
                "User",
                null,
                At);
            var client = Client.Create(
                ClientType.Company,
                "Synthetic client",
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
                11_520,
                "prequotes/document.pdf",
                owner.Id,
                At);
            var attempt = DocumentProcessingAttempt.Create(
                document.Id,
                owner.Id,
                Guid.NewGuid(),
                At);
            attempt.Start(At.AddMinutes(1));
            attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(3));

            var extractionResult = DocumentExtractionResult.Create(
                attempt.Id,
                "3.0",
                PdfClassification.PdfText,
                false,
                5,
                "pymupdf",
                15,
                "{\"a\":1}",
                At.AddMinutes(2));

            var glassType = GlassType.Create(
                "LAM_4_4",
                "Laminado 4+4",
                null,
                At);
            var priceRange = GlassPriceRangeVersion.Create(
                glassType.Id,
                1,
                90_000.123456m,
                100_000.123456m,
                110_000.123456m,
                "COP",
                GlassPriceRangeStatus.Preliminary,
                At,
                null,
                At);
            var extraction = StructuredDocumentExtraction.Create(
                extractionResult.Id,
                StructuredExtractionStatus.Completed,
                "Synthetic project",
                "Synthetic client",
                "Bogota",
                1,
                0,
                1,
                3,
                "rule_based_v2",
                5,
                [
                    new StructuredItemInput(
                        1,
                        "W-01",
                        "Synthetic window",
                        StructuredElementType.Window,
                        "1000 x 1500 mm",
                        1000,
                        1500,
                        3,
                        true,
                        new StructuredItemGlassInput(
                            glassType.Id,
                            "Laminado 4+4",
                            "LAM_4_4",
                            GlassAssignmentScope.Item,
                            true,
                            [GlassReviewReason.GlassTypeNotIdentified, GlassReviewReason.GlassTypeConflict],
                            [1, 2],
                            [
                                new StructuredItemGlassEvidenceInput(
                                    1, 1, EvidenceSourceType.Native, "Evidence #1"),
                                new StructuredItemGlassEvidenceInput(
                                    2, 2, EvidenceSourceType.Native, "Evidence #2")
                            ]),
                        new StructuredItemGlassValuationInput(
                            GlassValuationStatus.Valued,
                            null,
                            glassType.Id,
                            priceRange.Id,
                            1,
                            GlassPriceRangeStatus.Preliminary,
                            "COP",
                            1.500000m,
                            4.500000m,
                            90_000.123456m,
                            100_000.123456m,
                            110_000.123456m,
                            270_000.370368m,
                            450_000.555552m,
                            810_001.111104m))
                ],
                [],
                [],
                [],
                [],
                At.AddMinutes(2),
                1,
                1);

            ownerUserId = owner.Id;
            preQuoteId = preQuote.Id;
            documentId = document.Id;
            extractionId = extraction.Id;
            glassTypeId = glassType.Id;
            priceRangeId = priceRange.Id;

            context.AddRange(owner, client, project, preQuote, document, attempt,
                extractionResult, glassType, priceRange, extraction);
            await context.SaveChangesAsync(cancellationToken);
        }

        CreatePreQuoteDraftResult result;
        await using (var context = fixture.CreateDbContext())
        {
            if (owner is null)
            {
                throw new InvalidOperationException("El owner no fue creado correctamente.");
            }

            var repository = new PreQuoteDraftRepository(context);
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(ownerUserId);
            var identity = Substitute.For<IIdentityRepository>();
            identity.FindUserByIdAsync(ownerUserId, cancellationToken)
                .Returns(Task.FromResult<User?>(owner));

            var service = new CreatePreQuoteDraftService(
                new CreatePreQuoteDraftCommandValidator(),
                currentUser,
                identity,
                repository,
                new FixedProvider(At));

            result = await service.ExecuteAsync(
                new CreatePreQuoteDraftCommand(preQuoteId, documentId, extractionId),
                cancellationToken);
        }

        Assert.True(result.Draft is not null);
        Assert.True(result.IsSuccess);
        Assert.Equal(preQuoteId, result.Draft!.PreQuoteId);
        Assert.Equal(documentId, result.Draft.SourceDocumentId);
        Assert.Equal(extractionId, result.Draft.SourceStructuredExtractionId);
        var created = result.Draft;
        Assert.True(created.EconomicSummary.IsEconomicallyComplete);

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await new PreQuoteDraftRepository(verifyContext)
            .FindReadAsync(preQuoteId, ownerUserId, TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        var item = Assert.Single(persisted!.Items);
        Assert.NotNull(item.GlassSnapshot);
        Assert.NotNull(item.ValuationSnapshot);
        Assert.Equal(glassTypeId, item.GlassSnapshot!.GlassTypeId);
        Assert.Equal(priceRangeId, item.ValuationSnapshot!.GlassPriceRangeVersionId);
        Assert.Equal("LAM_4_4", item.GlassSnapshot.NormalizedCodeSnapshot);
        Assert.Equal("Laminado 4+4", item.GlassSnapshot.RawSpecification);
        Assert.True(item.GlassSnapshot.RequiresReview);
        Assert.Equal(
            [GlassReviewReason.GlassTypeNotIdentified, GlassReviewReason.GlassTypeConflict],
            item.GlassSnapshot.ReviewReasons.Select(x => x.Code).OrderBy(x => x).ToArray());
        var evidence = item.GlassSnapshot.Evidence.OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Equal("Evidence #1", evidence[0].Text);
        Assert.Equal("Evidence #2", evidence[1].Text);
        Assert.Equal(2, item.GlassSnapshot.SourcePages.Count);
        Assert.Equal([1, 2], item.GlassSnapshot.SourcePages.OrderBy(x => x.Sequence)
            .Select(x => x.PageNumber).ToArray());
        Assert.Equal(1.500000m, item.ValuationSnapshot.UnitAreaSquareMeters);
        Assert.Equal(4.500000m, item.ValuationSnapshot.TotalAreaSquareMeters);
        Assert.Equal(90_000.12m, item.ValuationSnapshot.UnitPricePerSquareMeter);
        Assert.Equal(270_000.37m, item.ValuationSnapshot.UnitAmount);
        Assert.Equal(810_001.11m, item.ValuationSnapshot.TotalAmount);
        Assert.Equal("COP", item.ValuationSnapshot.Currency);
    }

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
