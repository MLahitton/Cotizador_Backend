using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.UpdatePreQuoteName;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdatePreQuoteNameServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithName_UpdatesOnlyNameAndKeepsSerial()
    {
        var context = CreateContext("success");

        var result = await context.Service.ExecuteAsync(
            new UpdatePreQuoteNameCommand(context.PreQuote.Id, "  Cocina terraza - Apto 302  "),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("PC-2026-0001", context.PreQuote.Serial);
        Assert.Equal("Cocina terraza - Apto 302", context.PreQuote.Name);
        Assert.Equal("Cocina terraza - Apto 302", result.PreQuote?.Name);
        await context.PreQuotes.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_WithBlankName_ClearsName(string? name)
    {
        var context = CreateContext("success", initialName: "Initial");

        var result = await context.Service.ExecuteAsync(
            new UpdatePreQuoteNameCommand(context.PreQuote.Id, name),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(context.PreQuote.Name);
    }

    [Fact]
    public async Task Execute_WithTooLongName_ReturnsInvalidRequest()
    {
        var context = CreateContext("success");
        var tooLong = new string('A', PreQuote.MaxNameLength + 1);

        var result = await context.Service.ExecuteAsync(
            new UpdatePreQuoteNameCommand(context.PreQuote.Id, tooLong),
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdatePreQuoteNameFailure.InvalidRequest, result.Failure);
        await context.PreQuotes.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(string scenario, string? initialName = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        var client = Client.Create(ClientType.Company, "Client", null, null, null, null, null, null, null, UserId, At);
        var project = ProjectEntity.Create(client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2026-0001", initialName, At);
        var clock = new FixedTimeProvider(At.AddMinutes(1));

        currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        preQuotes.FindForUpdateByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "not_found" ? null : preQuote);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(_ => new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.Serial,
                preQuote.Name,
                0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        preQuotes.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = new UpdatePreQuoteNameService(
            new UpdatePreQuoteNameCommandValidator(),
            currentUser,
            identity,
            projects,
            preQuotes,
            clock);

        return new Context(service, preQuote, preQuotes);
    }

    private sealed record Context(
        UpdatePreQuoteNameService Service,
        PreQuote PreQuote,
        IPreQuoteRepository PreQuotes);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}