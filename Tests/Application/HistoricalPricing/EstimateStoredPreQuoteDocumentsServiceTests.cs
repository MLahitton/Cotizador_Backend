using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Storage;
using Application.HistoricalPricing;
using Domain.Identity;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class EstimateStoredPreQuoteDocumentsServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutSelection_UsesAllDocumentsInOnePipelineCall()
    {
        var context = Context(Documents(Document("a.pdf"), Document("b.xlsx")));
        IReadOnlyList<DocumentProcessingFile>? captured = null;
        context.Pipeline.EstimateAsync(
                Arg.Do<IReadOnlyList<DocumentProcessingFile>>(
                    files => captured = files),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(PipelineSuccess(2));

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(2, captured.Count);
        await context.Pipeline.Received(1).EstimateAsync(
            Arg.Any<IReadOnlyList<DocumentProcessingFile>>(),
            ProjectId,
            PreQuoteId,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WithSubset_UsesOnlySelectedDocument()
    {
        var selected = Document("selected.pdf");
        var context = Context(Documents(selected));

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            [selected.DocumentId],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await context.Repository.Received(1).GetForHistoricalEstimateAsync(
            PreQuoteId,
            context.User.Id,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1
                && ids[0] == selected.DocumentId),
            TestContext.Current.CancellationToken);
        await context.Pipeline.Received(1).EstimateAsync(
            Arg.Is<IReadOnlyList<DocumentProcessingFile>>(
                files => files.Count == 1 && files[0].DocumentId == selected.DocumentId),
            ProjectId,
            PreQuoteId,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WithDocumentOutsidePreQuote_ReturnsNotFound()
    {
        var context = Context(new StoredPreQuoteDocumentsReadModel(
            PreQuoteId, ProjectId, false, []));

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            [Guid.NewGuid()],
            TestContext.Current.CancellationToken);

        Assert.Equal(StoredPreQuoteHistoricalEstimateFailure.NotFound, result.Failure);
        await context.Pipeline.DidNotReceive().EstimateAsync(
            Arg.Any<IReadOnlyList<DocumentProcessingFile>>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingPreQuote_ReturnsNotFound()
    {
        var context = Context(null);

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(StoredPreQuoteHistoricalEstimateFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnavailablePhysicalFile_ReturnsControlledFailure()
    {
        var context = Context(Documents(Document("missing.pdf")));
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Stream>(
                new FileStorageReadException(new FileNotFoundException())));

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            StoredPreQuoteHistoricalEstimateFailure.FileUnavailable,
            result.Failure);
        await context.Pipeline.DidNotReceive().EstimateAsync(
            Arg.Any<IReadOnlyList<DocumentProcessingFile>>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRequirePreviousDocumentProcessing()
    {
        var document = Document("unprocessed.pdf");
        var context = Context(Documents(document));

        var result = await context.Service.ExecuteAsync(
            PreQuoteId,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await context.Pipeline.Received(1).EstimateAsync(
            Arg.Is<IReadOnlyList<DocumentProcessingFile>>(
                files => files.Single().DocumentId == document.DocumentId),
            ProjectId,
            PreQuoteId,
            TestContext.Current.CancellationToken);
    }

    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static TestData Context(StoredPreQuoteDocumentsReadModel? documents)
    {
        var user = User.CreateFromGoogle(
            "owner@example.com", "Owner", null, null, DateTimeOffset.UtcNow);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(user.Id);
        var identity = Substitute.For<IIdentityRepository>();
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        var repository = Substitute.For<IPreQuoteStoredDocumentRepository>();
        repository.GetForHistoricalEstimateAsync(
                PreQuoteId,
                user.Id,
                Arg.Any<IReadOnlyList<Guid>?>(),
                Arg.Any<CancellationToken>())
            .Returns(documents);
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var pipeline = Substitute.For<IHistoricalDocumentEstimatePipeline>();
        pipeline.EstimateAsync(
                Arg.Any<IReadOnlyList<DocumentProcessingFile>>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(PipelineSuccess(documents?.Documents.Count ?? 0));
        return new TestData(
            new EstimateStoredPreQuoteDocumentsService(
                currentUser, identity, repository, storage, pipeline),
            repository,
            storage,
            pipeline,
            user);
    }

    private static StoredPreQuoteDocumentsReadModel Documents(
        params StoredPreQuoteDocumentReadModel[] documents) =>
        new(PreQuoteId, ProjectId, true, documents);

    private static StoredPreQuoteDocumentReadModel Document(string name) =>
        new(
            Guid.NewGuid(),
            name,
            name.EndsWith(".xlsx", StringComparison.Ordinal)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/pdf",
            3,
            $"prequotes/{PreQuoteId}/{Guid.NewGuid()}/{name}");

    private static HistoricalDocumentEstimatePipelineResult PipelineSuccess(
        int sourceCount) =>
        new(
            HistoricalDocumentEstimatePipelineFailure.None,
            ProjectId,
            PreQuoteId,
            sourceCount,
            [],
            new PricedRequirementExtraction(
                0, 0, 0, 0, null, null, null, null, 0,
                HistoricalPriceConfidenceLevel.Low, false, false,
                [], [], [], []));

    private sealed record TestData(
        EstimateStoredPreQuoteDocumentsService Service,
        IPreQuoteStoredDocumentRepository Repository,
        IFileStorage Storage,
        IHistoricalDocumentEstimatePipeline Pipeline,
        User User);
}
