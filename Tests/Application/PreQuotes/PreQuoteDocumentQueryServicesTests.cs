using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetPreQuoteDocuments;
using Application.PreQuotes.GetStructuredDocumentExtraction;
using Domain.Identity;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class PreQuoteDocumentQueryServicesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EntityId = Guid.NewGuid();
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, 1, 20)]
    [InlineData(true, 0, 20)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 1, 101)]
    public async Task DocumentsValidator_ValidatesBounds(
        bool hasId,
        int page,
        int pageSize)
    {
        var validator = new GetPreQuoteDocumentsQueryValidator();

        var result = await validator.ValidateAsync(
            new GetPreQuoteDocumentsQuery(
                hasId ? EntityId : Guid.Empty,
                page,
                pageSize),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task DocumentsValidator_AcceptsValidValues()
    {
        var result = await new GetPreQuoteDocumentsQueryValidator()
            .ValidateAsync(
                new GetPreQuoteDocumentsQuery(EntityId, 1, 100),
                TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredValidator_ValidatesDocumentId(bool valid)
    {
        var result = await new GetStructuredDocumentExtractionQueryValidator()
            .ValidateAsync(
                new GetStructuredDocumentExtractionQuery(
                    valid ? EntityId : Guid.Empty),
                TestContext.Current.CancellationToken);

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("invalid", GetPreQuoteDocumentsFailure.InvalidRequest)]
    [InlineData("unauthenticated", GetPreQuoteDocumentsFailure.Unauthorized)]
    [InlineData("missing_user", GetPreQuoteDocumentsFailure.Unauthorized)]
    [InlineData("inactive", GetPreQuoteDocumentsFailure.InactiveUser)]
    [InlineData("missing", GetPreQuoteDocumentsFailure.NotFound)]
    [InlineData("query_error", GetPreQuoteDocumentsFailure.QueryError)]
    public async Task DocumentsService_MapsFailures(
        string scenario,
        GetPreQuoteDocumentsFailure expected)
    {
        var context = CreateContext();
        var validator = Substitute.For<IValidator<GetPreQuoteDocumentsQuery>>();
        validator.ValidateAsync(
                Arg.Any<GetPreQuoteDocumentsQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(
                scenario == "invalid"
                    ? [new ValidationFailure("x", "invalid")]
                    : []));
        ConfigureScenario(context, scenario);
        var service = new GetPreQuoteDocumentsService(
            validator, context.CurrentUser, context.Identity, context.Repository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteDocumentsQuery(EntityId, 1, 20),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task DocumentsService_ReturnsEmptyPage()
    {
        var context = CreateContext();
        context.Repository.GetDocumentsAsync(
                EntityId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDocumentsPageReadModel([], 0));
        var service = new GetPreQuoteDocumentsService(
            new GetPreQuoteDocumentsQueryValidator(),
            context.CurrentUser, context.Identity, context.Repository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteDocumentsQuery(EntityId, 1, 20),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Documents!.Items);
    }

    [Theory]
    [InlineData("invalid", GetStructuredDocumentExtractionFailure.InvalidRequest)]
    [InlineData("unauthenticated", GetStructuredDocumentExtractionFailure.Unauthorized)]
    [InlineData("missing_user", GetStructuredDocumentExtractionFailure.Unauthorized)]
    [InlineData("inactive", GetStructuredDocumentExtractionFailure.InactiveUser)]
    [InlineData("missing", GetStructuredDocumentExtractionFailure.NotFound)]
    [InlineData("query_error", GetStructuredDocumentExtractionFailure.QueryError)]
    public async Task DetailsService_MapsFailures(
        string scenario,
        GetStructuredDocumentExtractionFailure expected)
    {
        var context = CreateContext();
        var validator =
            Substitute.For<IValidator<GetStructuredDocumentExtractionQuery>>();
        validator.ValidateAsync(
                Arg.Any<GetStructuredDocumentExtractionQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(
                scenario == "invalid"
                    ? [new ValidationFailure("x", "invalid")]
                    : []));
        ConfigureScenario(context, scenario);
        var service = new GetStructuredDocumentExtractionService(
            validator, context.CurrentUser, context.Identity, context.Repository);

        var result = await service.ExecuteAsync(
            new GetStructuredDocumentExtractionQuery(EntityId),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task DetailsService_ReturnsUnprocessedDocument()
    {
        var context = CreateContext();
        context.Repository.GetStructuredExtractionAsync(
                EntityId, Arg.Any<CancellationToken>())
            .Returns(new StructuredDocumentExtractionQueryReadModel(
                new PreQuoteDocumentReadModel(
                    EntityId, Guid.NewGuid(), "a.pdf", "application/pdf",
                    1, CreatedAt),
                DocumentProcessingAvailability.NotProcessed,
                null,
                null));
        var service = new GetStructuredDocumentExtractionService(
            new GetStructuredDocumentExtractionQueryValidator(),
            context.CurrentUser, context.Identity, context.Repository);

        var result = await service.ExecuteAsync(
            new GetStructuredDocumentExtractionQuery(EntityId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Details!.StructuredExtraction);
    }

    private static Context CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IPreQuoteDocumentQueryRepository>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(CreateUser());
        return new Context(currentUser, identity, repository);
    }

    private static void ConfigureScenario(Context context, string scenario)
    {
        if (scenario == "unauthenticated")
        {
            context.CurrentUser.IsAuthenticated.Returns(false);
        }
        else if (scenario == "missing_user")
        {
            context.Identity.FindUserByIdAsync(
                    UserId, Arg.Any<CancellationToken>())
                .Returns((User?)null);
        }
        else if (scenario == "inactive")
        {
            var user = CreateUser();
            user.Deactivate(CreatedAt.AddMinutes(1));
            context.Identity.FindUserByIdAsync(
                    UserId, Arg.Any<CancellationToken>())
                .Returns(user);
        }
        else if (scenario == "query_error")
        {
            context.Repository.GetDocumentsAsync(
                    Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task<PreQuoteDocumentsPageReadModel?>>(
                    _ => throw new PreQuoteDocumentQueryException(
                        new InvalidOperationException()));
            context.Repository.GetStructuredExtractionAsync(
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<Task<StructuredDocumentExtractionQueryReadModel?>>(
                    _ => throw new PreQuoteDocumentQueryException(
                        new InvalidOperationException()));
        }
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "user@example.com", "Test", "User", null, CreatedAt);

    private sealed record Context(
        ICurrentUser CurrentUser,
        IIdentityRepository Identity,
        IPreQuoteDocumentQueryRepository Repository);
}
