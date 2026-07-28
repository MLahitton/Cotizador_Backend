using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetDocumentProcessingAttemptServiceTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AttemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DocumentProcessingState.Pending, null, false)]
    [InlineData(DocumentProcessingState.Processing, null, false)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.Completed, true)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.RequiresReview, true)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.Failed, false)]
    public async Task ExecuteAsync_ReturnsPersistedState(
        DocumentProcessingState state,
        DocumentProcessingOutcome? outcome,
        bool hasResult)
    {
        var context = new Context();
        context.Repository.FindAttemptStatusAsync(
                DocumentId,
                AttemptId,
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(new DocumentProcessingAttemptStatusSnapshot(
                AttemptId,
                DocumentId,
                state,
                outcome,
                outcome == DocumentProcessingOutcome.Failed ? "AI_SERVICE_TIMEOUT" : null,
                CreatedAt,
                state == DocumentProcessingState.Pending ? null : CreatedAt.AddSeconds(1),
                state == DocumentProcessingState.Finished ? CreatedAt.AddSeconds(2) : null,
                hasResult ? """{"schemaVersion":"1.0"}""" : null));

        var result = await context.Service.ExecuteAsync(
            DocumentId,
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Attempt);
        Assert.Equal(state, result.Attempt.ProcessingState);
        Assert.Equal(outcome, result.Attempt.Outcome);
        Assert.Equal(hasResult, result.Attempt.ResultPayloadJson is not null);
    }

    [Theory]
    [InlineData("invalid", GetDocumentProcessingAttemptFailure.InvalidRequest)]
    [InlineData("unauthorized", GetDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData("missing_user", GetDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData("inactive_user", GetDocumentProcessingAttemptFailure.InactiveUser)]
    [InlineData("not_found", GetDocumentProcessingAttemptFailure.NotFound)]
    [InlineData("query", GetDocumentProcessingAttemptFailure.QueryError)]
    public async Task ExecuteAsync_WithFailure_ReturnsSafeResult(
        string scenario,
        GetDocumentProcessingAttemptFailure failure)
    {
        var context = new Context();
        var documentId = DocumentId;

        switch (scenario)
        {
            case "invalid":
                documentId = Guid.Empty;
                break;
            case "unauthorized":
                context.CurrentUser.IsAuthenticated.Returns(false);
                break;
            case "missing_user":
                context.IdentityRepository.FindUserByIdAsync(
                        UserId,
                        Arg.Any<CancellationToken>())
                    .Returns((User?)null);
                break;
            case "inactive_user":
                var user = Context.CreateUser();
                user.Deactivate(CreatedAt.AddSeconds(1));
                context.IdentityRepository.FindUserByIdAsync(
                        UserId,
                        Arg.Any<CancellationToken>())
                    .Returns(user);
                break;
            case "query":
                context.Repository.FindAttemptStatusAsync(
                        DocumentId,
                        AttemptId,
                        UserId,
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<DocumentProcessingAttemptStatusSnapshot?>(
                        new DocumentProcessingQueryException(
                            new InvalidOperationException())));
                break;
        }

        var result = await context.Service.ExecuteAsync(
            documentId,
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Attempt);
    }

    private sealed class Context
    {
        public Context()
        {
            CurrentUser = Substitute.For<ICurrentUser>();
            IdentityRepository = Substitute.For<IIdentityRepository>();
            Repository = Substitute.For<IDocumentProcessingRepository>();
            CurrentUser.IsAuthenticated.Returns(true);
            CurrentUser.UserId.Returns(UserId);
            IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateUser());
            Repository.FindAttemptStatusAsync(
                    DocumentId,
                    AttemptId,
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns((DocumentProcessingAttemptStatusSnapshot?)null);
            Service = new GetDocumentProcessingAttemptService(
                CurrentUser,
                IdentityRepository,
                Repository);
        }

        public ICurrentUser CurrentUser { get; }
        public IIdentityRepository IdentityRepository { get; }
        public IDocumentProcessingRepository Repository { get; }
        public GetDocumentProcessingAttemptService Service { get; }

        public static User CreateUser() => User.CreateFromGoogle(
            "user@example.com",
            "Test",
            "User",
            null,
            CreatedAt);
    }
}
