using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using CotizadorBackend.Tests.TestDoubles;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ProcessClaimedDocumentProcessingAttemptServiceTests
{
    private static readonly Guid AttemptId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt =
        CreatedAt.AddSeconds(10);

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public async Task ProcessAsync_WithSuccess_FinalizesAndCreatesResult(
        DocumentProcessingOutcome outcome)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(outcome));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.Attempt.ProcessingState);
        Assert.Equal(outcome, context.Attempt.Outcome);
        Assert.Null(context.Attempt.ErrorCode);
        context.Repository.Received(1).AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DocumentProcessingClientFailure.Timeout, "AI_SERVICE_TIMEOUT")]
    [InlineData(DocumentProcessingClientFailure.ServiceUnavailable, "AI_SERVICE_UNAVAILABLE")]
    [InlineData(DocumentProcessingClientFailure.InvalidResponse, "AI_INVALID_RESPONSE")]
    public async Task ProcessAsync_WithClientFailure_FinalizesWithoutResult(
        DocumentProcessingClientFailure failure,
        string errorCode)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Failed(failure));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal(DocumentProcessingOutcome.Failed, context.Attempt.Outcome);
        Assert.Equal(errorCode, context.Attempt.ErrorCode);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("INVALID_REQUEST")]
    [InlineData("INVALID_CORRELATION_ID")]
    [InlineData("EMPTY_FILE")]
    [InlineData("INVALID_PDF")]
    [InlineData("PDF_PASSWORD_REQUIRED")]
    [InlineData("PDF_PAGE_LIMIT_EXCEEDED")]
    [InlineData("FILE_TOO_LARGE")]
    [InlineData("UNSUPPORTED_FILE_TYPE")]
    public async Task ProcessAsync_WithRemoteRejection_PersistsExactCode(
        string errorCode)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.RemoteFailure(
                DocumentProcessingClientFailure.RemoteRejection,
                new DocumentProcessingRemoteError(
                    422,
                    "1.0",
                    errorCode,
                    "Remote message.")));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal(errorCode, context.Attempt.ErrorCode);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
    }

    [Theory]
    [InlineData("invalid_key")]
    [InlineData("read_error")]
    public async Task ProcessAsync_WithStorageFailure_FinalizesFailed(
        string scenario)
    {
        var context = new Context();
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(scenario == "invalid_key"
                ? Task.FromException<Stream>(new InvalidStorageKeyException())
                : Task.FromException<Stream>(
                    new FileStorageReadException(new IOException())));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal("DOCUMENT_STORAGE_ERROR", context.Attempt.ErrorCode);
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("finished")]
    public async Task ProcessAsync_WithInvalidState_DoesNoExternalWork(
        string state)
    {
        var context = new Context(started: false);

        if (state == "finished")
        {
            context.Attempt.Start(CreatedAt.AddSeconds(1));
            context.Attempt.Fail("AI_SERVICE_TIMEOUT", CreatedAt.AddSeconds(2));
        }

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.InvalidState,
            result);
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithMissingAttempt_ReturnsNotFound()
    {
        var context = new Context();
        context.Repository.FindProcessingWorkItemAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns((DocumentProcessingWorkItem?)null);

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.NotFound,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WithQueryError_ReturnsQueryError()
    {
        var context = new Context();
        context.Repository.FindProcessingWorkItemAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DocumentProcessingWorkItem?>(
                new DocumentProcessingQueryException(
                    new InvalidOperationException())));

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.QueryError,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WithTerminalPersistenceError_DoesNotReportSuccess()
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(DocumentProcessingOutcome.Completed));
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingPersistenceException(
                    new InvalidOperationException())));

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.PersistenceError,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WhenHostCancels_LeavesProcessingAndRethrows()
    {
        var context = new Context();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<Stream>(cancellationSource.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Service.ProcessAsync(
                AttemptId,
                cancellationSource.Token));

        Assert.Equal(
            DocumentProcessingState.Processing,
            context.Attempt.ProcessingState);
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static DocumentProcessingClientResult CreateSuccess(
        DocumentProcessingOutcome outcome)
    {
        var requiresOcr = outcome == DocumentProcessingOutcome.RequiresReview;
        return DocumentProcessingClientResult.Success(
            new DocumentProcessingResponseData(
                "1.0",
                DocumentId,
                AttemptId,
                outcome,
                new ProcessedDocumentData(
                    "document.pdf",
                    "application/pdf",
                    100,
                    1,
                    requiresOcr
                        ? PdfClassification.PdfScanned
                        : PdfClassification.PdfText,
                    requiresOcr),
                [new ProcessedPageData(1, requiresOcr ? "" : "Text", requiresOcr ? 0 : 4, !requiresOcr)],
                [],
                new ProcessingMetadataData("pymupdf", 10),
                """{"schemaVersion":"1.0","status":"COMPLETED","document":{},"pages":[],"warnings":[],"processingMetadata":{}}"""));
    }

    private sealed class Context
    {
        public Context(bool started = true)
        {
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Storage = Substitute.For<IFileStorage>();
            Client = Substitute.For<IDocumentProcessingClient>();
            Attempt = DocumentProcessingAttempt.Create(
                DocumentId,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt);
            if (started)
            {
                Attempt.Start(CreatedAt.AddSeconds(1));
            }

            Source = new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "document.pdf",
                "application/pdf",
                100,
                "prequotes/document.pdf",
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                true,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                true);
            Repository.FindProcessingWorkItemAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => new DocumentProcessingWorkItem(Attempt, Source));
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Storage.OpenReadAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Stream>(
                    new MemoryStream([1, 2, 3])));
            Client.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(DocumentProcessingClientResult.Failed(
                    DocumentProcessingClientFailure.Timeout));
            Service = new ProcessClaimedDocumentProcessingAttemptService(
                Repository,
                Storage,
                Client,
                new FixedTimeProvider(CompletedAt));
        }

        public IDocumentProcessingRepository Repository { get; }
        public IFileStorage Storage { get; }
        public IDocumentProcessingClient Client { get; }
        public DocumentProcessingAttempt Attempt { get; }
        public DocumentProcessingSource Source { get; }
        public ProcessClaimedDocumentProcessingAttemptService Service { get; }
    }
}
