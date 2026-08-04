using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.GetPreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetPreQuoteDraftServiceOwnershipTests
{
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_ForeignOwner_DraftReadReturnsNotFound()
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

        Guid? ownerFromRead = null;
        repository.FindReadAsync(
                PreQuoteId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ownerFromRead = call.ArgAt<Guid>(1);
                return (PreQuoteDraft?)null;
            });

        var service = new GetPreQuoteDraftService(
            new GetPreQuoteDraftQueryValidator(),
            currentUser,
            identity,
            repository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteDraftQuery(PreQuoteId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        Assert.NotNull(ownerFromRead);
        Assert.Equal(user.Id, ownerFromRead);
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "owner@example.com", "Owner", "User", null, At);
}
