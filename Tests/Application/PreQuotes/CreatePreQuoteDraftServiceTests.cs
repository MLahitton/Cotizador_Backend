using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.CreatePreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreatePreQuoteDraftServiceOwnershipTests
{
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid ExtractionId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_ForeignSource_ReturnsNotFound_AndKeepsOwnershipSafeFlow()
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

        Guid? ownerFromSource = null;
        repository.FindSourceAsync(
                PreQuoteId,
                DocumentId,
                ExtractionId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ownerFromSource = call.ArgAt<Guid>(3);
                return (PreQuoteDraftSourceContext?)null;
            });

        var service = new CreatePreQuoteDraftService(
            new CreatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At.AddMinutes(1)));

        var result = await service.ExecuteAsync(
            new CreatePreQuoteDraftCommand(PreQuoteId, DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        Assert.NotNull(ownerFromSource);
        Assert.Equal(user.Id, ownerFromSource);
        await repository.DidNotReceive().ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "owner@example.com", "Owner", "User", null, At);

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
