using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using CotizadorBackend.Tests.TestDoubles;
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
    private static readonly Guid UserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private const string ApplicationPdfContentType = "application/pdf";
    private const string ApplicationXlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_EnqueuesPendingAttempt()
    {
        var context = new Context();

        var result = await context.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Attempt);
        Assert.Equal(DocumentId, result.Attempt.DocumentId);
        Assert.Equal(
            DocumentProcessingState.Pending,
            result.Attempt.ProcessingState);
        Assert.Null(result.Attempt.Outcome);
        Assert.Null(result.Attempt.ErrorCode);
        Assert.Null(result.Attempt.StartedAtUtc);
        Assert.Null(result.Attempt.CompletedAtUtc);
        Assert.Null(result.Attempt.ResultPayloadJson);
        Assert.Equal(Now, result.Attempt.CreatedAtUtc);
        Assert.NotNull(context.AddedAttempt);
        Assert.Equal(
            DocumentProcessingState.Pending,
            context.AddedAttempt.ProcessingState);
        context.Repository.Received(1).AddAttempt(context.AddedAttempt);
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
    }

    [Theory]
    [InlineData("document.pdf", ApplicationPdfContentType, "prequotes/document.pdf")]
    [InlineData("document.xlsx", ApplicationXlsxContentType, "prequotes/document.xlsx")]
    public async Task ExecuteAsync_WithValidSupportedMetadata_ForPdfAndXlsx_EnqueuesPendingAttempt(
        string fileName,
        string contentType,
        string storageKey)
    {
        var context = new Context
        {
            Source = Context.CreateSource(
                fileName,
                contentType,
                storageKey: storageKey)
        };

        var result = await context.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.AddedAttempt);
        context.Repository.Received(1).AddAttempt(context.AddedAttempt!);
    }

    [Theory]
    [InlineData("document.xlsx", ApplicationPdfContentType, "prequotes/document.xlsx")]
    [InlineData("document.pdf", ApplicationXlsxContentType, "prequotes/document.pdf")]
    [InlineData("document.xlsx", ApplicationPdfContentType, "prequotes/document.pdf")]
    [InlineData("document.pdf", ApplicationXlsxContentType, "prequotes/document.xlsx")]
    [InlineData("document.pdf", "text/plain", "prequotes/document.pdf")]
    public async Task ExecuteAsync_WithInvalidMetadataContentTypeOrExtensions_ReturnsQueryError(
        string fileName,
        string contentType,
        string storageKey)
    {
        var context = new Context
        {
            Source = Context.CreateSource(
                fileName,
                contentType,
                storageKey: storageKey)
        };

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure.QueryError);
        context.Repository.DidNotReceive().AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveAttempt_ReturnsExistingConflict()
    {
        var context = new Context();
        context.Repository.HasActiveDocumentProcessingAttemptAsync(
                DocumentId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive);
        context.Repository.DidNotReceive().AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithForeignProject_ReturnsNotFoundBeforeStateChecks()
    {
        var context = new Context
        {
            Source = Context.CreateSource(
                projectCreatedByUserId: Guid.NewGuid(),
                projectActive: false,
                clientActive: false)
        };

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure.DocumentNotFound);
        await context.Repository.DidNotReceive()
            .HasActiveDocumentProcessingAttemptAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithConcurrentConflict_ReturnsExistingConflict()
    {
        var context = new Context();
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingActiveAttemptConflictException(
                    new InvalidOperationException())));

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive);
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
    }

    [Fact]
    public async Task ExecuteAsync_WithPersistenceFailure_ReturnsInitialError()
    {
        var context = new Context();
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingPersistenceException(
                    new InvalidOperationException())));

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure.InitialPersistenceError);
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("file_name")]
    [InlineData("content_type")]
    [InlineData("size")]
    [InlineData("oversized")]
    [InlineData("storage_key")]
    [InlineData("trimmed_file_name")]
    [InlineData("trimmed_storage_key")]
    public async Task ExecuteAsync_WithInvalidMetadata_ReturnsQueryError(
        string scenario)
    {
        var context = new Context
        {
            Source = scenario switch
            {
                "file_name" => Context.CreateSource(fileName: ""),
                "content_type" => Context.CreateSource(contentType: "text/plain"),
                "size" => Context.CreateSource(sizeBytes: 0),
                "oversized" => Context.CreateSource(sizeBytes: long.MaxValue),
                "storage_key" => Context.CreateSource(storageKey: ""),
                "trimmed_file_name" => Context.CreateSource(fileName: " x.pdf "),
                "trimmed_storage_key" => Context.CreateSource(storageKey: " key "),
                _ => throw new InvalidOperationException()
            }
        };

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure.QueryError);
        context.Repository.DidNotReceive().AddAttempt(
            Arg.Any<DocumentProcessingAttempt>());
    }

    [Theory]
    [InlineData(false, true, CreateDocumentProcessingAttemptFailure.InactiveProject)]
    [InlineData(true, false, CreateDocumentProcessingAttemptFailure.InactiveClient)]
    public async Task ExecuteAsync_WithInactiveOwnership_ReturnsConflict(
        bool projectActive,
        bool clientActive,
        CreateDocumentProcessingAttemptFailure failure)
    {
        var context = new Context
        {
            Source = Context.CreateSource(
                projectActive: projectActive,
                clientActive: clientActive)
        };

        var result = await context.ExecuteAsync();

        AssertFailure(result, failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCommand_StopsImmediately()
    {
        var context = new Context();
        context.Validator.ValidateAsync(
                Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(
            [
                new ValidationFailure("DocumentId", "Required")
            ]));

        var result = await context.ExecuteAsync();

        AssertFailure(
            result,
            CreateDocumentProcessingAttemptFailure.InvalidRequest);
        await context.IdentityRepository.DidNotReceive()
            .FindUserByIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unauthenticated", CreateDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData("missing_user", CreateDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData("inactive_user", CreateDocumentProcessingAttemptFailure.InactiveUser)]
    public async Task ExecuteAsync_WithInvalidIdentity_ReturnsExpectedFailure(
        string scenario,
        CreateDocumentProcessingAttemptFailure failure)
    {
        var context = new Context();

        if (scenario == "unauthenticated")
        {
            context.CurrentUser.IsAuthenticated.Returns(false);
        }
        else if (scenario == "missing_user")
        {
            context.IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns((User?)null);
        }
        else
        {
            var user = Context.CreateUser();
            user.Deactivate(Now.AddSeconds(1));
            context.IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(user);
        }

        var result = await context.ExecuteAsync();

        AssertFailure(result, failure);
    }

    [Theory]
    [InlineData("missing", CreateDocumentProcessingAttemptFailure.DocumentNotFound)]
    [InlineData("query", CreateDocumentProcessingAttemptFailure.QueryError)]
    [InlineData("active_query", CreateDocumentProcessingAttemptFailure.QueryError)]
    public async Task ExecuteAsync_WithQueryFailure_ReturnsExpectedFailure(
        string scenario,
        CreateDocumentProcessingAttemptFailure failure)
    {
        var context = new Context();

        if (scenario == "missing")
        {
            context.Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns((DocumentProcessingSource?)null);
        }
        else if (scenario == "query")
        {
            context.Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<DocumentProcessingSource?>(
                    new DocumentProcessingQueryException(
                        new InvalidOperationException())));
        }
        else
        {
            context.Repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<bool>(
                    new DocumentProcessingQueryException(
                        new InvalidOperationException())));
        }

        var result = await context.ExecuteAsync();

        AssertFailure(result, failure);
    }

    private static void AssertFailure(
        CreateDocumentProcessingAttemptResult result,
        CreateDocumentProcessingAttemptFailure failure)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Attempt);
    }

    private sealed class Context
    {
        public Context()
        {
            Validator = Substitute.For<
                IValidator<CreateDocumentProcessingAttemptCommand>>();
            CurrentUser = Substitute.For<ICurrentUser>();
            IdentityRepository = Substitute.For<IIdentityRepository>();
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Validator.ValidateAsync(
                    Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
            CurrentUser.IsAuthenticated.Returns(true);
            CurrentUser.UserId.Returns(UserId);
            IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateUser());
            Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(false);
            Repository.When(repository => repository.AddAttempt(
                    Arg.Any<DocumentProcessingAttempt>()))
                .Do(call => AddedAttempt =
                    call.Arg<DocumentProcessingAttempt>());
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Service = new CreateDocumentProcessingAttemptService(
                Validator,
                CurrentUser,
                IdentityRepository,
                Repository,
                new FixedTimeProvider(Now));
        }

        public IValidator<CreateDocumentProcessingAttemptCommand> Validator { get; }
        public ICurrentUser CurrentUser { get; }
        public IIdentityRepository IdentityRepository { get; }
        public IDocumentProcessingRepository Repository { get; }
        public CreateDocumentProcessingAttemptService Service { get; }
        public DocumentProcessingAttempt? AddedAttempt { get; private set; }
        public DocumentProcessingSource Source { get; set; } = CreateSource();

        public Task<CreateDocumentProcessingAttemptResult> ExecuteAsync()
        {
            return Service.ExecuteAsync(
                new CreateDocumentProcessingAttemptCommand(DocumentId),
                TestContext.Current.CancellationToken);
        }

        public static User CreateUser()
        {
            return User.CreateFromGoogle(
                "user@example.com",
                "Test",
                "User",
                null,
                Now);
        }

        public static DocumentProcessingSource CreateSource(
            string fileName = "document.pdf",
            string contentType = "application/pdf",
            long sizeBytes = 100,
            string storageKey = "prequotes/document.pdf",
            Guid? projectCreatedByUserId = null,
            bool projectActive = true,
            bool clientActive = true)
        {
            return new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                fileName,
                contentType,
                sizeBytes,
                storageKey,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                projectCreatedByUserId ?? UserId,
                projectActive,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                clientActive);
        }
    }
}
