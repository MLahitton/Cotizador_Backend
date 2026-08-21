using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Application.PreQuotes.ProcessRequirement;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ProcessRequirementServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private const string PdfContentType = "application/pdf";
    private const string PngContentType = "image/png";

    [Fact]
    public async Task Execute_WithValidRequirement_CallsAi2AndPersistsExtraction()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        RequirementProcessingAttempt? attempt = null;
        RequirementExtractionResult? extraction = null;
        RequirementExtractedItem? extractedItem = null;
        RequirementExtractedItemEvidence? extractedEvidence = null;
        RequirementTechnicalProposal? proposal = null;
        context.Requirements.When(repository => repository.AddProcessingAttempt(
                Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => attempt = call.Arg<RequirementProcessingAttempt>());
        context.Requirements.When(repository => repository.AddExtractionResult(
                Arg.Any<RequirementExtractionResult>()))
            .Do(call => extraction = call.Arg<RequirementExtractionResult>());
        context.Requirements.When(repository => repository.AddExtractedItem(
                Arg.Any<RequirementExtractedItem>()))
            .Do(call => extractedItem = call.Arg<RequirementExtractedItem>());
        context.Requirements.When(repository => repository.AddExtractedItemEvidence(
                Arg.Any<RequirementExtractedItemEvidence>()))
            .Do(call => extractedEvidence = call.Arg<RequirementExtractedItemEvidence>());
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.Completed, result.Attempt!.Outcome);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        Assert.NotNull(attempt);
        Assert.NotNull(extraction);
        Assert.Equal(attempt!.Id, extraction!.RequirementProcessingAttemptId);
        Assert.Equal("Ai2", extraction.Provider);
        Assert.Equal(15, extraction.ItemCount);
        Assert.Equal(0, extraction.ItemsRequiringReview);
        Assert.Equal("{\"requirement\":{},\"elements\":[]}", extraction.PayloadJson);
        Assert.NotNull(extractedItem);
        Assert.Equal(extraction.Id, extractedItem!.RequirementExtractionResultId);
        Assert.Equal("element-pv06", extractedItem.Ai2ElementId);
        Assert.Equal("PV-06", extractedItem.Reference);
        Assert.Equal("SLIDING_DOOR", extractedItem.FunctionalType);
        Assert.Equal("3831", extractedItem.RequestedSystemRaw);
        Assert.Equal("templado", extractedItem.GlassTypeNormalized);
        Assert.Equal(6m, extractedItem.GlassThicknessMm);
        Assert.Equal("negro pintura al horno", extractedItem.FinishRawDescription);
        Assert.Equal("PAINTED", extractedItem.FinishNormalizedType);
        Assert.Equal("BLACK", extractedItem.FinishColorNormalized);
        Assert.NotNull(extractedEvidence);
        Assert.Equal(extractedItem.Id, extractedEvidence!.RequirementExtractedItemId);
        Assert.Equal("A12:H12", extractedEvidence.CellRange);
        Assert.NotNull(proposal);
        var proposalItem = Assert.Single(proposal!.Items);
        Assert.NotNull(proposalItem.SuggestedSystemId);
        Assert.NotNull(proposalItem.SuggestedGlassTypeId);
        Assert.NotNull(proposalItem.SuggestedFinishTypeId);
        Assert.False(proposalItem.RequiresReview);
        Assert.True(proposalItem.IsTechnicallyComplete);
        Assert.True(proposalItem.IsPriceable);
        await context.Requirements.Received(3).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithMultipleFiles_SendsFilesProjectAndRequirementToAi2()
    {
        DocumentProcessingClientRequest? captured = null;
        bool? allStreamsWereReadableDuringAiCall = null;
        var first = File("source.pdf", PdfContentType);
        var second = File("photo.png", PngContentType);
        var context = CreateContext("success", first, second);
        context.Ai2.ProcessAsync(
                Arg.Do<DocumentProcessingClientRequest>(request =>
                {
                    captured = request;
                    allStreamsWereReadableDuringAiCall =
                        request.Files.All(file => file.Content.CanRead);
                }),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DocumentProcessingClientResult.Success(
                CreateResponse(call.Arg<DocumentProcessingClientRequest>()))));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(first.Id, captured!.DocumentId);
        Assert.Equal(context.Project.Id, captured.ProjectId);
        Assert.Equal(context.Requirement.Id, captured.RequirementId);
        Assert.Equal(2, captured.Files.Count);
        Assert.Equal([first.Id, second.Id], captured.Files.Select(file => file.DocumentId));
        Assert.Equal(["source.pdf", "photo.png"], captured.Files.Select(file => file.FileName));
        Assert.True(allStreamsWereReadableDuringAiCall);
    }

    [Fact]
    public async Task Execute_WithAi2RequiresReview_CompletesAttemptAndMarksRequirementProcessed()
    {
        var context = CreateContext("requires_review", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Attempt!.Outcome);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        Assert.Equal(1, result.Attempt.Summary!.ItemsRequiringReview);
    }

    [Fact]
    public async Task Execute_WithAi2RawGlassSignal_DoesNotRequireBackendCatalogCode()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await context.Ai2.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        context.Requirements.Received(1).AddExtractionResult(
            Arg.Is<RequirementExtractionResult>(extraction =>
                extraction.ItemCount == 15
                && extraction.Provider == "Ai2"));
    }

    [Fact]
    public async Task Execute_WithMissingGlassSignal_AppliesAuditableTemp5Default()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext("missing_glass", File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.NotNull(item.SuggestedGlassTypeId);
        Assert.True(item.RequiresReview);
        Assert.Contains("HISTORICAL_DEFAULT_GLASS", item.ReviewReasons);
        Assert.Contains("HISTORICAL_DEFAULT_GLASS", item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithMissingFinishSignal_AppliesPp13DefaultWithoutReview()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext("missing_finish", File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.NotNull(item.SuggestedFinishTypeId);
        Assert.False(item.RequiresReview);
        Assert.DoesNotContain("FINISH_NOT_SPECIFIED", item.ReviewReasons);
        Assert.Contains("HISTORICAL_DEFAULT_FINISH", item.FinishResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithInvalidEvidenceLocation_DoesNotPersistInvalidEvidence()
    {
        var context = CreateContext(
            "invalid_evidence_location",
            File("source.pdf", PdfContentType));
        RequirementExtractedItem? extractedItem = null;
        context.Requirements.When(repository => repository.AddExtractedItem(
                Arg.Any<RequirementExtractedItem>()))
            .Do(call => extractedItem = call.Arg<RequirementExtractedItem>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Attempt!.Outcome);
        Assert.NotNull(extractedItem);
        Assert.True(extractedItem!.RequiresReview);
        Assert.Contains("INVALID_EVIDENCE_LOCATION", extractedItem.ReviewReasons);
        context.Requirements.DidNotReceive().AddExtractedItemEvidence(
            Arg.Any<RequirementExtractedItemEvidence>());
    }

    [Fact]
    public async Task Execute_WhenTechnicalProposalCatalogThrows_FinalizesFailureLifecycle()
    {
        var context = CreateContext(
            "product_system_query_error",
            File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProcessRequirementFailure.PersistenceError, result.Failure);
        Assert.NotNull(result.Attempt);
        Assert.Equal(
            DocumentProcessingState.Finished,
            result.Attempt!.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, result.Attempt.Outcome);
        Assert.Equal("REQUIREMENT_PERSISTENCE_ERROR", result.Attempt.ErrorCode);
        Assert.NotEqual(
            DocumentProcessingState.Processing,
            result.Attempt.ProcessingState);
        Assert.Equal(RequirementStatus.Failed, context.Requirement.Status);
        await context.Requirements.Received(1).FinalizeProcessingFailureAsync(
            context.Requirement.Id,
            result.Attempt.ProcessingAttemptId,
            "REQUIREMENT_PERSISTENCE_ERROR",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenRequirementIsProcessing_ReturnsConflict()
    {
        var context = CreateContext("already_processing", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProcessRequirementFailure.AlreadyProcessing, result.Failure);
        await context.Ai2.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenRequirementAlreadyProcessed_AllowsReprocessing()
    {
        var context = CreateContext("processed", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        await context.Ai2.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not_found", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData("no_files", ProcessRequirementFailure.NoFiles)]
    [InlineData("storage_error", ProcessRequirementFailure.StorageError)]
    [InlineData("ai_unavailable", ProcessRequirementFailure.AiServiceUnavailable)]
    [InlineData("ai_timeout", ProcessRequirementFailure.AiTimeout)]
    [InlineData("ai_invalid", ProcessRequirementFailure.AiInvalidResponse)]
    public async Task Execute_WithFailure_ReturnsExpectedFailure(
        string scenario,
        ProcessRequirementFailure expected)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
        if (expected is ProcessRequirementFailure.StorageError
            or ProcessRequirementFailure.AiServiceUnavailable
            or ProcessRequirementFailure.AiTimeout
            or ProcessRequirementFailure.AiInvalidResponse)
        {
            Assert.NotNull(result.Attempt);
            Assert.Equal(DocumentProcessingOutcome.Failed, result.Attempt!.Outcome);
        }
    }

    private static Context CreateContext(
        string scenario,
        params RequirementFile[] files)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var storage = Substitute.For<IFileStorage>();
        var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
        var user = User.CreateFromGoogle(
            "user@example.com", "User", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, UserId, At);
        var project = ProjectEntity.Create(
            client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, At);
        var requirement = Requirement.Create(preQuote.Id, UserId, At.AddMinutes(1));
        RequirementProcessingAttempt? activeAttempt = null;

        if (scenario == "already_processing")
        {
            requirement.StartProcessing(At.AddMinutes(2));
        }
        else if (scenario == "processed")
        {
            requirement.StartProcessing(At.AddMinutes(2));
            requirement.MarkProcessed(At.AddMinutes(3));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindByIdAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(scenario == "not_found" ? null : requirement);
        requirements.ListFilesByRequirementIdAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(scenario == "no_files" ? [] : files);
        preQuotes.FindForUpdateByIdAsync(
                preQuote.Id,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);
        storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => scenario == "storage_error"
                ? Task.FromException<Stream>(new FileStorageReadException(
                    new IOException("sensitive")))
                : Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4])));

        ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => scenario switch
            {
                "ai_unavailable" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.ServiceUnavailable)),
                "ai_timeout" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.Timeout)),
                "ai_invalid" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.InvalidResponse)),
                "requires_review" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            DocumentProcessingOutcome.RequiresReview,
                            1))),
                "missing_glass" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            omitGlassSignal: true))),
                "missing_finish" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            omitFinishSignal: true))),
                "invalid_evidence_location" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            invalidEvidenceLocation: true))),
                _ => Task.FromResult(DocumentProcessingClientResult.Success(
                    CreateResponse(call.Arg<DocumentProcessingClientRequest>())))
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        requirements.When(repository => repository.AddProcessingAttempt(
                Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => activeAttempt =
                call.Arg<RequirementProcessingAttempt>());
        requirements.FinalizeProcessingFailureAsync(
                requirement.Id,
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var errorCode = call.ArgAt<string>(2);
                var completedAtUtc = call.ArgAt<DateTimeOffset>(3);
                if (activeAttempt is not null
                    && activeAttempt.ProcessingState
                        == DocumentProcessingState.Processing)
                {
                    activeAttempt.Fail(errorCode, completedAtUtc);
                }

                if (requirement.Status == RequirementStatus.Processing)
                {
                    requirement.MarkFailed(completedAtUtc);
                }

                preQuote.RegisterActivity(completedAtUtc);
                return new RequirementProcessingFailureFinalization(
                    requirement.Id,
                    call.ArgAt<Guid>(1),
                    activeAttempt?.CorrelationId ?? Guid.NewGuid(),
                    DocumentProcessingState.Finished,
                    DocumentProcessingOutcome.Failed,
                    errorCode,
                    activeAttempt?.StartedAtUtc ?? completedAtUtc,
                    completedAtUtc);
            });
        var productSystems = Substitute.For<IProductSystemCatalogRepository>();
        productSystems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns(call => scenario == "product_system_query_error"
                ? Task.FromException<IReadOnlyList<ProductSystemCatalogReadModel>>(
                    new InvalidOperationException(
                        "The LINQ expression could not be translated."))
                : Task.FromResult<IReadOnlyList<ProductSystemCatalogReadModel>>(
                    [ProductSystem("K70", "SLIDING_DOOR", "VENECIA NAPOLES")]));
        var glassCatalog = Substitute.For<IGlassTypeCatalogRepository>();
        glassCatalog.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns([
                Glass("TEMP_6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC"),
                Glass("TEMP_5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC")
            ]);
        var finishCatalog = Substitute.For<IFinishTypeCatalogRepository>();
        finishCatalog.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([Finish("BLACK_MATTE", "ALUCOLOR POLIESTER NEGRO MATE PP13")]);
        var similarity = Substitute.For<IHistoricalSimilarityEvaluationService>();
        similarity.EvaluateAsync(Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalSimilarityEvaluationResult(
                HistoricalSimilarityStatus.Completed,
                [],
                null));
        similarity.EvaluateBatchAsync(
                Arg.Any<IReadOnlyList<HistoricalSimilarityBatchQuery>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var queries = call.Arg<IReadOnlyList<HistoricalSimilarityBatchQuery>>();
                return Task.FromResult<IReadOnlyDictionary<string, HistoricalSimilarityEvaluationResult>>(
                    queries.ToDictionary(
                        value => value.RequestId,
                        _ => new HistoricalSimilarityEvaluationResult(
                            HistoricalSimilarityStatus.Completed,
                            [],
                            null),
                        StringComparer.Ordinal));
            });
        var technicalProposal = new BuildRequirementTechnicalProposalService(
            requirements,
            productSystems,
            glassCatalog,
            finishCatalog,
            new GlassCandidateResolver(),
            new FinishCandidateResolver(),
            new ResolveHistoricalTechnicalEvidenceService(
                similarity,
                productSystems,
                new DeterministicSgTechnicalSelector(productSystems)));

        var service = new ProcessRequirementService(
            new ProcessRequirementCommandValidator(),
            currentUser,
            identity,
            preQuotes,
            projects,
            clients,
            requirements,
            storage,
            ai2,
            technicalProposal,
            new FixedTimeProvider(At.AddMinutes(10)),
            Substitute.For<ILogger<ProcessRequirementService>>());

        return new Context(
            service,
            requirement,
            project,
            requirements,
            ai2);
    }

    private static ProductSystemCatalogReadModel ProductSystem(
        string code,
        string functionalType,
        string family) =>
        new(
            Guid.NewGuid(),
            code,
            $"PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO {family}",
            $"PUERTA CORREDIZA SISTEMA VENECIA SERIE 70 {family}",
            family,
            functionalType,
            family,
            "SERIE 70",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string name)
    {
        var thickness = code.EndsWith("_5", StringComparison.Ordinal)
            ? 5m
            : 6m;
        return
        new(
            Guid.NewGuid(),
            code,
            name,
            null,
            true,
            null,
            Family: "MONOLITHIC",
            Composition: "TEMPERED",
            Treatment: "TEMPERED",
            OuterThicknessMm: thickness,
            IsSelectable: true);
    }

    private static FinishTypeCatalogReadModel Finish(
        string code,
        string name) =>
        new(
            Guid.NewGuid(),
            code,
            name,
            "PAINTED",
            "BLACK",
            "MATTE",
            "PAINTED",
            null,
            "ALUMINUM",
            true,
            false,
            true);

    private static RequirementFile File(
        string fileName,
        string contentType)
    {
        return RequirementFile.Create(
            Guid.NewGuid(),
            fileName,
            contentType,
            4,
            $"requirements/{Guid.NewGuid():D}/{fileName}",
            At.AddMinutes(1));
    }

    private static DocumentProcessingResponseData CreateResponse(
        DocumentProcessingClientRequest request,
        DocumentProcessingOutcome outcome = DocumentProcessingOutcome.Completed,
        int itemsRequiringReview = 0,
        bool invalidEvidenceLocation = false,
        bool omitGlassSignal = false,
        bool omitFinishSignal = false)
    {
        var status = outcome == DocumentProcessingOutcome.RequiresReview
            ? StructuredExtractionStatus.RequiresReview
            : StructuredExtractionStatus.Completed;
        var item = new StructuredItemData(
            1,
            "PV-06",
            "Puerta vidriera",
            StructuredElementType.Door,
            "3740 x 2500",
            3740,
            2500,
            1,
            itemsRequiringReview > 0,
            [],
            [],
            [],
            new StructuredItemGlassData(
                omitGlassSignal ? null : "templado 6 mm",
                omitGlassSignal ? null : "templado",
                GlassAssignmentScope.Item,
                false,
                [],
                [],
                [
                    new SourceEvidenceData(
                        invalidEvidenceLocation ? 0 : null,
                        invalidEvidenceLocation
                            ? EvidenceSourceType.Native
                            : EvidenceSourceType.Xlsx,
                        "PV-06 Puerta vidriera",
                        invalidEvidenceLocation ? null : "Cotizacion",
                        invalidEvidenceLocation ? null : "A12:H12",
                        "source-1",
                        0.95m)
                ]),
            null,
            9.35m,
            "corrediza",
            0.92m,
            CanonicalExtractionValueStatus.Explicit,
            "SLIDING_DOOR",
            "SLIDING",
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            "element-pv06",
            "TWO_PANELS",
            "3831",
            "3831",
            omitGlassSignal ? null : "templado",
            omitGlassSignal ? null : "templado",
            omitGlassSignal ? null : 6m,
            null,
            null,
            null,
            null,
            omitGlassSignal ? null : "monolitico",
            null,
            null,
            omitFinishSignal ? null : "negro pintura al horno",
            omitFinishSignal ? null : "PAINTED",
            null,
            omitFinishSignal ? null : "BLACK",
            null,
            omitFinishSignal ? null : "MATTE",
            null,
            false);
        var structuredExtraction = new StructuredExtractionData(
            status,
            "Proyecto",
            "Cliente",
            "Bogota",
            [],
            [],
            [],
            [item],
            [],
            [],
            [],
            15,
            0,
            itemsRequiringReview,
            15,
            "ai2_requirement_extraction",
            1234,
            1,
            itemsRequiringReview);

        return new DocumentProcessingResponseData(
            "AI2-1.0",
            request.DocumentId,
            request.ProcessingAttemptId,
            outcome,
            new ProcessedDocumentData(
                request.Files[0].FileName,
                request.Files[0].ContentType,
                request.Files.Sum(file => file.SizeBytes),
                0,
                DocumentClassification.Xlsx,
                false),
            [],
            [],
            new ProcessingMetadataData("ai2", 1234),
            "{\"requirement\":{},\"elements\":[]}",
            structuredExtraction,
            DocumentProcessingProvider.Ai2);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record Context(
        ProcessRequirementService Service,
        Requirement Requirement,
        ProjectEntity Project,
        IRequirementRepository Requirements,
        IAi2DocumentProcessingClient Ai2);
}
