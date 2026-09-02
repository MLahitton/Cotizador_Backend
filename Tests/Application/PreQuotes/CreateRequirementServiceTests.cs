using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreateRequirementServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private const string PdfContentType = "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JpegContentType = "image/jpeg";

    [Fact]
    public async Task Execute_WithSinglePdf_CreatesRequirementAndFile()
    {
        var context = CreateContext("success");
        Requirement? requirement = null;
        RequirementFile? file = null;
        context.Requirements.When(repository => repository.Add(
                Arg.Any<Requirement>()))
            .Do(call => requirement = call.Arg<Requirement>());
        context.Requirements.When(repository => repository.AddFile(
                Arg.Any<RequirementFile>()))
            .Do(call => file = call.Arg<RequirementFile>());

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(context.PreQuote.Id, "ESSENTIAL", [CreateFile("requirement.pdf", PdfContentType)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("PENDING", result.Requirement!.Status);
        Assert.Equal(1, result.Requirement.FileCount);
        Assert.NotNull(requirement);
        Assert.NotNull(file);
        Assert.Equal(RequirementCommercialLine.Essential, requirement!.CommercialLine);
        Assert.Equal(requirement!.Id, file!.RequirementId);
        Assert.Equal(PdfContentType, file.ContentType);
        Assert.EndsWith("/original.pdf", file.StorageKey, StringComparison.Ordinal);
        await context.Storage.Received(1).SaveAsync(
            file.StorageKey,
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
        await context.Requirements.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CLASSIC", RequirementCommercialLine.Classic)]
    [InlineData("ESSENTIAL", RequirementCommercialLine.Essential)]
    [InlineData("BIOCONFORT", RequirementCommercialLine.Bioconfort)]
    [InlineData("SIGNATURE", RequirementCommercialLine.Signature)]
    public async Task Execute_WithSupportedCommercialLine_PersistsCommercialLine(
        string commercialLine,
        RequirementCommercialLine expected)
    {
        var context = CreateContext("success");
        Requirement? requirement = null;
        context.Requirements.When(repository => repository.Add(
                Arg.Any<Requirement>()))
            .Do(call => requirement = call.Arg<Requirement>());

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(
                context.PreQuote.Id,
                commercialLine,
                [CreateFile("requirement.pdf", PdfContentType)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(commercialLine, result.Requirement!.CommercialLine);
        Assert.Equal(expected, requirement!.CommercialLine);
    }

    [Fact]
    public async Task Execute_WithPdfJpgXlsx_CreatesThreeRequirementFiles()
    {
        var context = CreateContext("success");
        var files = new List<RequirementFile>();
        context.Requirements.When(repository => repository.AddFile(
                Arg.Any<RequirementFile>()))
            .Do(call => files.Add(call.Arg<RequirementFile>()));

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(context.PreQuote.Id, "ESSENTIAL", [
                    CreateFile("requirement.pdf", PdfContentType),
                    CreateFile("photo.jpg", JpegContentType),
                    CreateFile("schedule.xlsx", XlsxContentType)
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Requirement!.FileCount);
        Assert.Equal(
            [PdfContentType, JpegContentType, XlsxContentType],
            files.Select(file => file.ContentType).ToArray());
        await context.Storage.Received(3).SaveAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("empty_prequote", CreateRequirementFailure.InvalidRequest)]
    [InlineData("no_files", CreateRequirementFailure.InvalidRequest)]
    [InlineData("empty_file", CreateRequirementFailure.EmptyFile)]
    [InlineData("unsupported", CreateRequirementFailure.UnsupportedFileType)]
    [InlineData("too_large", CreateRequirementFailure.FileTooLarge)]
    [InlineData("too_many", CreateRequirementFailure.TooManyFiles)]
    [InlineData("missing_commercial_line", CreateRequirementFailure.InvalidRequest)]
    [InlineData("invalid_commercial_line", CreateRequirementFailure.InvalidRequest)]
    [InlineData("unauthorized", CreateRequirementFailure.Unauthorized)]
    [InlineData("inactive_user", CreateRequirementFailure.InactiveUser)]
    [InlineData("prequote_not_found", CreateRequirementFailure.PreQuoteNotFound)]
    [InlineData("foreign", CreateRequirementFailure.PreQuoteNotFound)]
    [InlineData("inactive_project", CreateRequirementFailure.InactiveProject)]
    [InlineData("inactive_client", CreateRequirementFailure.InactiveClient)]
    public async Task Execute_WithInvalidInput_ReturnsExpectedFailure(
        string scenario,
        CreateRequirementFailure expected)
    {
        var context = CreateContext(scenario);
        var files = scenario switch
        {
            "no_files" => [],
            "empty_file" => [CreateFile("requirement.pdf", PdfContentType, 0)],
            "unsupported" => [CreateFile("requirement.txt", "text/plain")],
            "too_large" => [CreateFile(
                "requirement.pdf",
                PdfContentType,
                CreateRequirementService.MaximumFileSizeBytes + 1)],
            "too_many" => Enumerable.Range(0, CreateRequirementService.MaximumFileCount + 1)
                .Select(index => CreateFile($"requirement-{index}.pdf", PdfContentType))
                .ToArray(),
            _ => [CreateFile("requirement.pdf", PdfContentType)]
        };

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(
                scenario == "empty_prequote"
                    ? Guid.Empty
                    : context.PreQuote.Id,
                scenario == "missing_commercial_line"
                    ? null
                    : scenario == "invalid_commercial_line"
                        ? "premium"
                        : "ESSENTIAL",
                files),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
        await context.Requirements.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenSecondStorageFails_DeletesPreviouslyStoredFiles()
    {
        var context = CreateContext("storage_second_failure");

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(context.PreQuote.Id, "ESSENTIAL", [
                    CreateFile("first.pdf", PdfContentType),
                    CreateFile("second.jpg", JpegContentType)
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CreateRequirementFailure.StorageError, result.Failure);
        await context.Storage.Received(1).DeleteIfExistsAsync(
            Arg.Is<string>(key => key.EndsWith(
                "/original.pdf",
                StringComparison.Ordinal)),
            CancellationToken.None);
        await context.Requirements.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenPersistenceFails_DeletesAllStoredFiles()
    {
        var context = CreateContext("persistence");

        var result = await context.Service.ExecuteAsync(
            new CreateRequirementCommand(context.PreQuote.Id, "ESSENTIAL", [
                    CreateFile("first.pdf", PdfContentType),
                    CreateFile("second.jpg", JpegContentType)
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CreateRequirementFailure.PersistenceError, result.Failure);
        await context.Storage.Received(2).DeleteIfExistsAsync(
            Arg.Any<string>(),
            CancellationToken.None);
    }

    private static Context CreateContext(string scenario)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var storage = Substitute.For<IFileStorage>();
        var user = User.CreateFromGoogle(
            "user@example.com", "User", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, UserId, At);
        var owner = scenario == "foreign" ? Guid.NewGuid() : UserId;
        var project = ProjectEntity.Create(
            client.Id, "P-001", "Project", null, null, owner, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2020-0001", null, At);

        currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
        currentUser.UserId.Returns(UserId);
        if (scenario == "inactive_user")
        {
            user.Deactivate(At.AddMinutes(1));
        }
        if (scenario == "inactive_project")
        {
            project.SetActive(false, owner, At.AddMinutes(1));
        }
        if (scenario == "inactive_client")
        {
            client.SetActive(false, UserId, At.AddMinutes(1));
        }

        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        preQuotes.FindForUpdateByIdAsync(
                preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "prequote_not_found" ? null : preQuote);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var saveCallCount = 0;
        storage.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                saveCallCount++;
                return scenario == "storage_second_failure"
                    && saveCallCount == 2
                        ? Task.FromException(new FileStorageWriteException(
                            new IOException("sensitive")))
                        : Task.CompletedTask;
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(scenario == "persistence"
                ? Task.FromException(new RequirementPersistenceException(
                    new InvalidOperationException("sensitive")))
                : Task.CompletedTask);

        var service = new CreateRequirementService(
            new CreateRequirementCommandValidator(),
            currentUser,
            identity,
            preQuotes,
            projects,
            clients,
            requirements,
            storage,
            new FixedTimeProvider(At),
            Substitute.For<ILogger<CreateRequirementService>>());

        return new Context(service, preQuote, requirements, storage);
    }

    private static CreateRequirementFileInput CreateFile(
        string fileName,
        string contentType,
        long sizeBytes = 4)
    {
        return new CreateRequirementFileInput(
            fileName,
            contentType,
            sizeBytes,
            new MemoryStream(new byte[Math.Max(1, (int)Math.Min(sizeBytes, 4))]));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record Context(
        CreateRequirementService Service,
        PreQuote PreQuote,
        IRequirementRepository Requirements,
        IFileStorage Storage);
}
