using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ManageRequirementDocumentsServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private const string PdfContentType = "application/pdf";

    [Fact]
    public async Task CancelAsync_WithPendingRequirement_MarksRequirementNotCurrent()
    {
        var context = CreateContext();

        var result = await context.Service.CancelAsync(
            new CancelRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementStatus.Cancelled, context.Requirement.Status);
        Assert.False(context.Requirement.IsActive);
        Assert.False(result.Requirement!.IsCurrent);
        Assert.False(result.Requirement.CanEditDocuments);
        Assert.False(result.Requirement.CanCancel);
        await context.Requirements.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceRequirementAsync_WithProcessedRequirement_CreatesCurrentReplacement()
    {
        var context = CreateContext("processed");
        Requirement? replacement = null;
        context.Requirements.When(repository => repository.Add(
                Arg.Any<Requirement>()))
            .Do(call => replacement = call.Arg<Requirement>());

        var result = await context.Service.ReplaceRequirementAsync(
            new ReplaceRequirementCommand(
                context.Requirement.Id,
                [CreateFile("replacement.pdf", PdfContentType)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(replacement);
        Assert.Equal(RequirementStatus.Superseded, context.Requirement.Status);
        Assert.False(context.Requirement.IsCurrent);
        Assert.Equal(replacement!.Id, context.Requirement.SupersededByRequirementId);
        Assert.Equal(context.Requirement.Id, replacement.SupersedesRequirementId);
        Assert.True(result.Requirement!.IsCurrent);
        Assert.True(result.Requirement.CanEditDocuments);
        Assert.Equal(1, result.Requirement.FileCount);
        await context.Storage.Received(1).SaveAsync(
            Arg.Is<string>(key => key.EndsWith(
                "/original.pdf",
                StringComparison.Ordinal)),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
        await context.Requirements.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceRequirementAsync_WithPendingRequirement_ReturnsNotReplaceable()
    {
        var context = CreateContext();

        var result = await context.Service.ReplaceRequirementAsync(
            new ReplaceRequirementCommand(
                context.Requirement.Id,
                [CreateFile("replacement.pdf", PdfContentType)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ManageRequirementDocumentsFailure.RequirementNotReplaceable,
            result.Failure);
        await context.Requirements.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(string scenario = "pending")
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var storage = Substitute.For<IFileStorage>();
        var documentReads = new List<RequirementDocumentReadModel>();

        var user = User.CreateFromGoogle(
            "user@example.com",
            "User",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Client",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            UserId,
            At);
        var project = ProjectEntity.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            null,
            UserId,
            At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2020-0001", null, At);
        var requirement = Requirement.Create(
            preQuote.Id,
            UserId,
            RequirementCommercialLine.Essential,
            At);

        if (scenario == "processed")
        {
            requirement.StartProcessing(At.AddSeconds(1));
            requirement.MarkProcessed(At.AddSeconds(2));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindByIdForUpdateAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(requirement);
        requirements.When(repository => repository.AddFile(
                Arg.Any<RequirementFile>()))
            .Do(call =>
            {
                var file = call.Arg<RequirementFile>();
                documentReads.Add(new RequirementDocumentReadModel(
                    file.Id,
                    file.OriginalFileName,
                    file.ContentType,
                    file.SizeBytes,
                    file.CreatedAtUtc));
            });
        requirements.ListDocumentReadModelsByRequirementIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => documentReads
                .OrderBy(file => file.CreatedAtUtc)
                .ThenBy(file => file.RequirementFileId)
                .ToArray());
        preQuotes.FindForUpdateByIdAsync(
                preQuote.Id,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);
        storage.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var service = new ManageRequirementDocumentsService(
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients,
            storage,
            new FixedTimeProvider(At.AddSeconds(3)),
            Substitute.For<ILogger<ManageRequirementDocumentsService>>());

        return new Context(service, requirement, requirements, storage);
    }

    private static CreateRequirementFileInput CreateFile(
        string fileName,
        string contentType) =>
        new(
            fileName,
            contentType,
            4,
            new MemoryStream([1, 2, 3, 4]));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record Context(
        ManageRequirementDocumentsService Service,
        Requirement Requirement,
        IRequirementRepository Requirements,
        IFileStorage Storage);
}
