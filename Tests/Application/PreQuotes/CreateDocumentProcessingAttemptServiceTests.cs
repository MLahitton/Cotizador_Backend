using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreateDocumentProcessingAttemptServiceTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AttemptId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid CorrelationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid UserId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CompletedAtUtc =
        new(2026, 7, 25, 12, 0, 5, TimeSpan.Zero);

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public void Success_WithSuccessfulOutcome_SetsSuccessFlag(
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreateAttemptResult(outcome);

        var result = CreateDocumentProcessingAttemptResult.Success(attempt);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
        Assert.Same(attempt, result.Attempt);
        Assert.Equal(CreateDocumentProcessingAttemptFailure.None, result.Failure);
    }

    [Fact]
    public void Success_WithFailedOutcome_ThrowsArgumentException()
    {
        var attempt = CreateAttemptResult(
            DocumentProcessingOutcome.Failed,
            "AI_SERVICE_TIMEOUT");

        Assert.Throws<ArgumentException>(
            () => CreateDocumentProcessingAttemptResult.Success(attempt));
    }

    [Fact]
    public void ProcessingFailed_WithFailedAttempt_SetsProcessingFailureFlag()
    {
        var attempt = CreateAttemptResult(
            DocumentProcessingOutcome.Failed,
            "AI_SERVICE_TIMEOUT");

        var result =
            CreateDocumentProcessingAttemptResult.ProcessingFailed(attempt);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsProcessingFailure);
        Assert.Same(attempt, result.Attempt);
        Assert.Equal(CreateDocumentProcessingAttemptFailure.None, result.Failure);
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public void ProcessingFailed_WithSuccessfulOutcome_ThrowsArgumentException(
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreateAttemptResult(outcome);

        Assert.Throws<ArgumentException>(() =>
            CreateDocumentProcessingAttemptResult.ProcessingFailed(attempt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessingFailed_WithInvalidErrorCode_ThrowsArgumentException(
        string? errorCode)
    {
        var attempt = CreateAttemptResult(
            DocumentProcessingOutcome.Failed,
            errorCode);

        Assert.Throws<ArgumentException>(() =>
            CreateDocumentProcessingAttemptResult.ProcessingFailed(attempt));
    }

    [Fact]
    public void Failed_WithNone_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.None));
    }

    [Theory]
    [InlineData(CreateDocumentProcessingAttemptFailure.InvalidRequest)]
    [InlineData(CreateDocumentProcessingAttemptFailure.QueryError)]
    [InlineData(CreateDocumentProcessingAttemptFailure.FinalPersistenceError)]
    public void Failed_WithApplicationFailure_HasNoAttempt(
        CreateDocumentProcessingAttemptFailure failure)
    {
        var result = CreateDocumentProcessingAttemptResult.Failed(failure);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
        Assert.Null(result.Attempt);
        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRequest_StopsBeforeIdentityLookup()
    {
        var context = new ServiceContext();
        context.Validator.ValidateAsync(
                Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new ValidationResult(
                [
                    new ValidationFailure(
                        nameof(CreateDocumentProcessingAttemptCommand.DocumentId),
                        "DocumentId is required.")
                ]));

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.InvalidRequest);
        await context.IdentityRepository.DidNotReceive().FindUserByIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await context.Repository.DidNotReceive().FindDocumentSourceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnauthenticatedUser_StopsBeforeDocumentLookup()
    {
        var context = new ServiceContext();
        context.CurrentUser.IsAuthenticated.Returns(false);

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.Unauthorized);
        await context.IdentityRepository.DidNotReceive().FindUserByIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await context.Repository.DidNotReceive().FindDocumentSourceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownUser_StopsBeforeDocumentLookup()
    {
        var context = new ServiceContext();
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.Unauthorized);
        await context.Repository.DidNotReceive().FindDocumentSourceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveUser_StopsBeforeDocumentLookup()
    {
        var context = new ServiceContext();
        var user = CreateActiveUser();
        user.Deactivate(CompletedAtUtc);
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.InactiveUser);
        await context.Repository.DidNotReceive().FindDocumentSourceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingDocument_StopsBeforeAttemptCreation()
    {
        var context = new ServiceContext();
        context.Repository.FindDocumentSourceAsync(
                DocumentId,
                Arg.Any<CancellationToken>())
            .Returns((DocumentProcessingSource?)null);

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.DocumentNotFound);
        await AssertNoAttemptWorkAsync(context);
    }

    [Theory]
    [InlineData(false, true, CreateDocumentProcessingAttemptFailure.InactiveProject)]
    [InlineData(true, false, CreateDocumentProcessingAttemptFailure.InactiveClient)]
    public async Task ExecuteAsync_WithInactiveOwnership_StopsBeforeAttemptCreation(
        bool projectIsActive,
        bool clientIsActive,
        CreateDocumentProcessingAttemptFailure expectedFailure)
    {
        var context = new ServiceContext
        {
            Source = ServiceContext.CreateSource(
                projectIsActive: projectIsActive,
                clientIsActive: clientIsActive)
        };

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(result, expectedFailure);
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithQueryError_StopsBeforeAttemptCreation()
    {
        var context = new ServiceContext();
        context.Repository.FindDocumentSourceAsync(
                DocumentId,
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<DocumentProcessingSource?>(
                    new DocumentProcessingQueryException(
                        new InvalidOperationException("Query failed."))));

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.QueryError);
        await AssertNoAttemptWorkAsync(context);
    }

    [Theory]
    [InlineData("empty_file_name")]
    [InlineData("trimmed_file_name")]
    [InlineData("content_type")]
    [InlineData("zero_size")]
    [InlineData("oversized")]
    [InlineData("empty_storage_key")]
    [InlineData("trimmed_storage_key")]
    public async Task ExecuteAsync_WithInvalidPersistedMetadata_ReturnsQueryError(
        string scenario)
    {
        var context = new ServiceContext
        {
            Source = scenario switch
            {
                "empty_file_name" =>
                    ServiceContext.CreateSource(originalFileName: string.Empty),
                "trimmed_file_name" =>
                    ServiceContext.CreateSource(
                        originalFileName: " document.pdf "),
                "content_type" =>
                    ServiceContext.CreateSource(contentType: "text/plain"),
                "zero_size" =>
                    ServiceContext.CreateSource(sizeBytes: 0),
                "oversized" =>
                    ServiceContext.CreateSource(sizeBytes: long.MaxValue),
                "empty_storage_key" =>
                    ServiceContext.CreateSource(storageKey: string.Empty),
                "trimmed_storage_key" =>
                    ServiceContext.CreateSource(
                        storageKey: " prequotes/document.pdf "),
                _ => throw new InvalidOperationException()
            }
        };

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure.QueryError);
        await AssertNoAttemptWorkAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveAttempt_StopsBeforeAttemptCreation()
    {
        var context = new ServiceContext();
        context.Repository.HasActiveDocumentProcessingAttemptAsync(
                DocumentId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive);
        context.Repository.DidNotReceive().AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithConcurrentActiveAttemptConflict_StopsBeforeStorage()
    {
        var context = new ServiceContext();
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingActiveAttemptConflictException(
                    new InvalidOperationException("Concurrent insert."))));

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        AssertPreviousFailure(
            result,
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive);
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public async Task ExecuteAsync_WithSuccessfulResponse_PersistsResult(
        DocumentProcessingOutcome outcome)
    {
        var context = new ServiceContext();
        context.ClientResult = CreateSuccessfulClientResult(outcome);
        context.ApplyClientResult();

        using var cancellationSource = new CancellationTokenSource();
        var result = await context.ExecuteAsync(cancellationSource.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
        Assert.NotNull(result.Attempt);
        Assert.Equal(outcome, result.Attempt.Outcome);
        Assert.NotNull(context.AddedAttempt);
        Assert.NotNull(context.AddedResult);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.AddedAttempt.ProcessingState);
        Assert.NotNull(context.AddedAttempt.StartedAtUtc);
        Assert.Null(context.AddedAttempt.ErrorCode);
        Assert.Equal(
            [
                DocumentProcessingState.Pending,
                DocumentProcessingState.Processing,
                DocumentProcessingState.Finished
            ],
            context.PersistedStates);
        context.Repository.Received(1).AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.Received(2).SaveChangesAsync(
            cancellationSource.Token);
        await context.Repository.Received(1).SaveChangesAsync(
            CancellationToken.None);
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
    public async Task ExecuteAsync_WithRecognizedRemoteRejection_PersistsExactCode(
        string errorCode)
    {
        var context = new ServiceContext();
        context.ClientResult = DocumentProcessingClientResult.RemoteFailure(
            DocumentProcessingClientFailure.RemoteRejection,
            new DocumentProcessingRemoteError(
                422,
                "1.0",
                errorCode,
                "Remote message."));
        context.ApplyClientResult();

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsProcessingFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(errorCode, result.Attempt?.ErrorCode);
        Assert.Equal(DocumentProcessingOutcome.Failed, result.Attempt?.Outcome);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.AddedAttempt?.ProcessingState);
        Assert.Null(context.AddedResult);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Storage.DidNotReceive().DeleteIfExistsAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(
        DocumentProcessingClientFailure.InvalidResponse,
        "AI_INVALID_RESPONSE")]
    [InlineData(
        DocumentProcessingClientFailure.ServiceError,
        "AI_SERVICE_ERROR")]
    [InlineData(
        DocumentProcessingClientFailure.ServiceUnavailable,
        "AI_SERVICE_UNAVAILABLE")]
    [InlineData(
        DocumentProcessingClientFailure.Timeout,
        "AI_SERVICE_TIMEOUT")]
    public async Task ExecuteAsync_WithClientFailure_PersistsMappedCode(
        DocumentProcessingClientFailure failure,
        string expectedErrorCode)
    {
        var context = new ServiceContext();
        context.ClientResult = failure == DocumentProcessingClientFailure.ServiceError
            ? DocumentProcessingClientResult.RemoteFailure(
                failure,
                new DocumentProcessingRemoteError(
                    500,
                    "1.0",
                    "INTERNAL_SERVER_ERROR",
                    "An unexpected error occurred."))
            : DocumentProcessingClientResult.Failed(failure);
        context.ApplyClientResult();

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsProcessingFailure);
        Assert.Equal(expectedErrorCode, result.Attempt?.ErrorCode);
        Assert.NotNull(result.Attempt?.CompletedAtUtc);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.AddedAttempt?.ProcessingState);
        Assert.Null(context.AddedResult);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownRemoteCode_PersistsInvalidResponse()
    {
        var context = new ServiceContext();
        context.ClientResult = DocumentProcessingClientResult.RemoteFailure(
            DocumentProcessingClientFailure.RemoteRejection,
            new DocumentProcessingRemoteError(
                422,
                "1.0",
                "UNKNOWN_REMOTE_CODE",
                "Unknown."));
        context.ApplyClientResult();

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsProcessingFailure);
        Assert.Equal("AI_INVALID_RESPONSE", result.Attempt?.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithStorageFailure_PersistsStorageError()
    {
        var context = new ServiceContext();
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Stream>(
                new FileStorageReadException(
                    new IOException("Read failed."))));

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsProcessingFailure);
        Assert.Equal("DOCUMENT_STORAGE_ERROR", result.Attempt?.ErrorCode);
        Assert.Equal(
            [
                DocumentProcessingState.Pending,
                DocumentProcessingState.Processing,
                DocumentProcessingState.Finished
            ],
            context.PersistedStates);
        Assert.Null(context.AddedResult);
    }

    [Fact]
    public async Task ExecuteAsync_WithInitialPersistenceFailure_HasNoAttemptResult()
    {
        var context = new ServiceContext();
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingPersistenceException(
                    new InvalidOperationException("Write failed."))));

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            CreateDocumentProcessingAttemptFailure.InitialPersistenceError,
            result.Failure);
        Assert.Null(result.Attempt);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenProcessingPersistenceFails_StopsBeforeStorage()
    {
        var context = new ServiceContext();
        var saveCallCount = 0;
        var snapshots = new List<(
            DocumentProcessingState ProcessingState,
            DateTimeOffset? StartedAtUtc,
            DocumentProcessingOutcome? Outcome,
            DateTimeOffset? CompletedAtUtc,
            string? ErrorCode)>();
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCallCount++;
                var attempt = Assert.IsType<DocumentProcessingAttempt>(
                    context.AddedAttempt);
                snapshots.Add((
                    attempt.ProcessingState,
                    attempt.StartedAtUtc,
                    attempt.Outcome,
                    attempt.CompletedAtUtc,
                    attempt.ErrorCode));

                return saveCallCount == 2
                    ? Task.FromException(
                        new DocumentProcessingPersistenceException(
                            new InvalidOperationException(
                                "Processing write failed.")))
                    : Task.CompletedTask;
            });
        using var cancellationSource = new CancellationTokenSource();

        var result = await context.ExecuteAsync(cancellationSource.Token);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
        Assert.Equal(
            CreateDocumentProcessingAttemptFailure.InitialPersistenceError,
            result.Failure);
        Assert.Null(result.Attempt);
        context.Repository.Received(1).AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
        await context.Repository.Received(2).SaveChangesAsync(
            cancellationSource.Token);
        Assert.Equal(2, saveCallCount);
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(DocumentProcessingState.Pending, snapshots[0].ProcessingState);
        Assert.Null(snapshots[0].StartedAtUtc);
        Assert.Null(snapshots[0].Outcome);
        Assert.Null(snapshots[0].CompletedAtUtc);
        Assert.Null(snapshots[0].ErrorCode);
        Assert.Equal(
            DocumentProcessingState.Processing,
            snapshots[1].ProcessingState);
        Assert.NotNull(snapshots[1].StartedAtUtc);
        Assert.Null(snapshots[1].Outcome);
        Assert.Null(snapshots[1].CompletedAtUtc);
        Assert.Null(snapshots[1].ErrorCode);
        Assert.Equal(
            DocumentProcessingState.Processing,
            context.AddedAttempt?.ProcessingState);
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WithFinalPersistenceFailure_ReturnsApplicationFailure()
    {
        var context = new ServiceContext();
        var saveCall = 0;
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCall++;
            return saveCall < 3
                ? Task.CompletedTask
                    : Task.FromException(
                        new DocumentProcessingPersistenceException(
                            new InvalidOperationException("Write failed.")));
            });

        var result = await context.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            CreateDocumentProcessingAttemptFailure.FinalPersistenceError,
            result.Failure);
        Assert.Null(result.Attempt);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancels_PersistsCancellationAndRethrows()
    {
        var context = new ServiceContext();
        using var cancellationSource = new CancellationTokenSource();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                return Task.FromCanceled<DocumentProcessingClientResult>(
                    cancellationSource.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.ExecuteAsync(cancellationSource.Token));

        Assert.NotNull(context.AddedAttempt);
        Assert.Equal("REQUEST_CANCELLED", context.AddedAttempt.ErrorCode);
        Assert.Equal(
            DocumentProcessingOutcome.Failed,
            context.AddedAttempt.Outcome);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.AddedAttempt.ProcessingState);
        await context.Repository.Received(1).SaveChangesAsync(
            CancellationToken.None);
    }

    private static CreatedDocumentProcessingAttemptResult CreateAttemptResult(
        DocumentProcessingOutcome outcome,
        string? errorCode = null)
    {
        return new CreatedDocumentProcessingAttemptResult(
            AttemptId,
            DocumentId,
            CorrelationId,
            outcome,
            errorCode,
            outcome == DocumentProcessingOutcome.Failed ? null : "1.0",
            outcome == DocumentProcessingOutcome.Failed
                ? null
                : PdfClassification.PdfText,
            outcome == DocumentProcessingOutcome.Failed ? null : false,
            outcome == DocumentProcessingOutcome.Failed ? null : 1,
            0,
            outcome == DocumentProcessingOutcome.Failed ? null : "pymupdf",
            outcome == DocumentProcessingOutcome.Failed ? null : 15,
            CreatedAtUtc,
            CompletedAtUtc);
    }

    private static User CreateActiveUser()
    {
        return User.CreateFromGoogle(
            "user@example.com",
            "Test",
            "User",
            null,
            CreatedAtUtc);
    }

    private static void AssertPreviousFailure(
        CreateDocumentProcessingAttemptResult result,
        CreateDocumentProcessingAttemptFailure expectedFailure)
    {
        Assert.Equal(expectedFailure, result.Failure);
        Assert.Null(result.Attempt);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsProcessingFailure);
    }

    private static async Task AssertNoAttemptWorkAsync(ServiceContext context)
    {
        context.Repository.DidNotReceive().AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static DocumentProcessingClientResult CreateSuccessfulClientResult(
        DocumentProcessingOutcome outcome)
    {
        var classification = outcome == DocumentProcessingOutcome.Completed
            ? PdfClassification.PdfText
            : PdfClassification.PdfScanned;
        var requiresOcr = outcome == DocumentProcessingOutcome.RequiresReview;
        IReadOnlyList<ProcessingWarningData> warnings = requiresOcr
            ? [
                new ProcessingWarningData(
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    [1])
            ]
            : [];

        return DocumentProcessingClientResult.Success(
            new DocumentProcessingResponseData(
                "1.0",
                DocumentId,
                AttemptId,
                outcome,
                new ProcessedDocumentData(
                    "document.pdf",
                    "application/pdf",
                    4,
                    1,
                    classification,
                    requiresOcr),
                [
                    new ProcessedPageData(
                        1,
                        requiresOcr ? string.Empty : "Page 1",
                        requiresOcr ? 0 : 6,
                        !requiresOcr)
                ],
                warnings,
                new ProcessingMetadataData("pymupdf", 15),
                "{}"));
    }

    private sealed class ServiceContext
    {
        public ServiceContext()
        {
            Validator = Substitute.For<
                IValidator<CreateDocumentProcessingAttemptCommand>>();
            CurrentUser = Substitute.For<ICurrentUser>();
            IdentityRepository = Substitute.For<IIdentityRepository>();
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Storage = Substitute.For<IFileStorage>();
            Client = Substitute.For<IDocumentProcessingClient>();

            Validator.ValidateAsync(
                    Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
            CurrentUser.IsAuthenticated.Returns(true);
            CurrentUser.UserId.Returns(UserId);
            IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateActiveUser());
            Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(false);
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Repository.When(repository => repository.SaveChangesAsync(
                    Arg.Any<CancellationToken>()))
                .Do(_ =>
                {
                    if (AddedAttempt is not null)
                    {
                        PersistedStates.Add(
                            AddedAttempt.ProcessingState);
                    }
                });
            Repository.When(repository => repository.AddAttempt(
                    Arg.Any<DocumentProcessingAttempt>()))
                .Do(call => AddedAttempt =
                    call.Arg<DocumentProcessingAttempt>());
            Repository.When(repository => repository.AddResult(
                    Arg.Any<DocumentExtractionResult>()))
                .Do(call => AddedResult =
                    call.Arg<DocumentExtractionResult>());
            Storage.OpenReadAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Assert.Equal(
                        [
                            DocumentProcessingState.Pending,
                            DocumentProcessingState.Processing
                        ],
                        PersistedStates);
                    return Task.FromResult<Stream>(
                        new MemoryStream([1, 2, 3, 4]));
                });

            ClientResult = CreateSuccessfulClientResult(
                DocumentProcessingOutcome.Completed);
            ApplyClientResult();

            Service = new CreateDocumentProcessingAttemptService(
                Validator,
                CurrentUser,
                IdentityRepository,
                Repository,
                Storage,
                Client);
        }

        public IValidator<CreateDocumentProcessingAttemptCommand> Validator
        {
            get;
        }

        public ICurrentUser CurrentUser { get; }

        public IIdentityRepository IdentityRepository { get; }

        public IDocumentProcessingRepository Repository { get; }

        public IFileStorage Storage { get; }

        public IDocumentProcessingClient Client { get; }

        public CreateDocumentProcessingAttemptService Service { get; }

        public DocumentProcessingClientResult ClientResult { get; set; }

        public DocumentProcessingAttempt? AddedAttempt { get; private set; }

        public DocumentExtractionResult? AddedResult { get; private set; }

        public List<DocumentProcessingState> PersistedStates { get; } = [];

        public DocumentProcessingSource Source { get; set; } = CreateSource();

        public static DocumentProcessingSource CreateSource(
            string originalFileName = "document.pdf",
            string contentType = "application/pdf",
            long sizeBytes = 4,
            string storageKey = "prequotes/document.pdf",
            bool projectIsActive = true,
            bool clientIsActive = true)
        {
            return new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                originalFileName,
                contentType,
                sizeBytes,
                storageKey,
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                projectIsActive,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                clientIsActive);
        }

        public void ApplyClientResult()
        {
            Client.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Assert.Equal(
                        [
                            DocumentProcessingState.Pending,
                            DocumentProcessingState.Processing
                        ],
                        PersistedStates);
                    return Task.FromResult(ClientResult);
                });
        }

        public Task<CreateDocumentProcessingAttemptResult> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            return Service.ExecuteAsync(
                new CreateDocumentProcessingAttemptCommand(DocumentId),
                cancellationToken);
        }
    }
}
