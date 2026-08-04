using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ApprovePreQuoteDraftServiceOwnershipTests
{
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_ForeignOwner_DraftApproveReturnsNotFound()
    {
        var user = CreateUser();
        var currentUserId = user.Id;
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IPreQuoteDraftRepository>();

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(currentUserId);
        identity.FindUserByIdAsync(currentUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        Guid? ownerFromActivity = null;
        Guid? ownerFromDraft = null;
        repository.FindActivityAsync(
                PreQuoteId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ownerFromActivity = call.ArgAt<Guid>(1);
                return new PreQuoteDraftActivityContext(true, true);
            });
        repository.FindForUpdateAsync(
                PreQuoteId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ownerFromDraft = call.ArgAt<Guid>(1);
                return (PreQuoteDraft?)null;
            });

        var service = new ApprovePreQuoteDraftService(
            new ApprovePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At.AddMinutes(1)));

        var result = await service.ExecuteAsync(
            new ApprovePreQuoteDraftCommand(PreQuoteId, 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        Assert.NotNull(ownerFromActivity);
        Assert.NotNull(ownerFromDraft);
        Assert.Equal(user.Id, ownerFromActivity);
        Assert.Equal(user.Id, ownerFromDraft);
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "owner@example.com", "Owner", "User", null, At);

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
