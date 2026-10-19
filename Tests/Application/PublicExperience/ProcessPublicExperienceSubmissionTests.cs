using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Projects.CreateProject;
using Application.PublicExperience;
using Domain.Clients;
using Domain.Identity;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PublicExperience;

public sealed class ProcessPublicExperienceSubmissionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoDocuments_RetryFailsBeforeOwnerLookupOrBusinessEntities()
    {
        var submission = Submission(withFile: false);
        var identities = Identities();
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var service = Service(new(submission), identities, clients, projects);
        for (var retry = 0; retry < 2; retry++)
        {
            var result = await service.ExecuteAsync(submission.SubmissionId, Token);
            Assert.Equal(PublicExperienceRequirementPreparationErrors.DocumentsRequired, result!.ErrorCode);
            Assert.Equal("FAILED", result.Status);
            Assert.Null(result.ClientId);
            Assert.Null(result.ProjectId);
        }
        Assert.Empty(identities.ReceivedCalls());
        Assert.Empty(clients.ReceivedCalls());
        Assert.Empty(projects.ReceivedCalls());
    }

    [Theory]
    [InlineData("ANDRES_CANDELA")]
    [InlineData("VALERIA_ALVAREZ")]
    [InlineData("ALEJANDRA_DEL_RIO")]
    [InlineData("JULIAN_SIERRA")]
    [InlineData("PAULA_CHICA")]
    [InlineData("DIRECT_CONTACT")]
    public async Task MapsClientAndProject_AlwaysToConfiguredOwner(string advisor)
    {
        var submission = Submission(advisor: advisor, subdivision: "Ruitoque", lot: "Lote 14");
        var owner = Owner();
        var identities = Substitute.For<IIdentityRepository>();
        identities.FindUserByNormalizedEmailForUpdateAsync("owner@example.test", Token).Returns(owner);
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var repository = new ProcessingMemoryRepository(submission);
        var service = Service(repository, identities, clients, projects, "  OWNER@EXAMPLE.TEST ");
        var result = await service.ExecuteAsync(submission.SubmissionId, Token);
        var client = Assert.IsType<Client>(Assert.Single(clients.ReceivedCalls()).GetArguments()[0]);
        var project = Assert.IsType<Project>(Assert.Single(projects.ReceivedCalls()).GetArguments()[0]);
        Assert.Equal(ClientType.Person, client.ClientType);
        Assert.Equal(submission.ClientFullName, client.LegalName);
        Assert.Equal(submission.ClientWhatsapp, client.Phone);
        Assert.Null(client.TradeName);
        Assert.Null(client.DocumentType);
        Assert.Null(client.DocumentNumber);
        Assert.Null(client.Email);
        Assert.Null(client.Address);
        Assert.Null(client.City);
        Assert.Equal(owner.Id, client.CreatedByUserId);
        Assert.Equal(owner.Id, project.CreatedByUserId);
        Assert.Equal(client.Id, project.ClientId);
        Assert.Equal(submission.ProjectName, project.Name);
        Assert.Equal(submission.ProjectNotes, project.Description);
        Assert.Equal("Bucaramanga \u00b7 Ruitoque \u00b7 Lote 14", project.Location);
        Assert.Matches("^WEB-[0-9A-F]{24}$", project.Code);
        Assert.Equal($"WEB-{submission.Id:N}"[..28].ToUpperInvariant(), project.Code);
        Assert.True((await new CreateProjectCommandValidator().ValidateAsync(new CreateProjectCommand(
            client.Id, project.Code, project.Name, project.Description, project.Location), Token)).IsValid);
        Assert.Equal("PROCESSED", result!.Status);
        Assert.Equal(client.Id, result.ClientId);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.NotNull(submission.ProcessedAtUtc);
        Assert.Null(submission.ProcessingErrorCode);
        Assert.Equal(advisor, submission.AdvisorCode);
        await identities.Received(1).FindUserByNormalizedEmailForUpdateAsync("owner@example.test", Token);
    }

    [Theory]
    [InlineData(null, null, "Bucaramanga")]
    [InlineData("", " ", "Bucaramanga")]
    [InlineData("Ruitoque", null, "Bucaramanga \u00b7 Ruitoque")]
    [InlineData(null, "Lote 14", "Bucaramanga \u00b7 Lote 14")]
    public async Task Location_OmitsEmptyComponents(string? subdivision, string? lot, string expected)
    {
        var submission = Submission(subdivision: subdivision, lot: lot);
        var projects = Substitute.For<IProjectRepository>();
        var identities = Identities();
        await Service(new(submission), identities, Substitute.For<IClientRepository>(), projects)
            .ExecuteAsync(submission.SubmissionId, Token);
        projects.Received(1).Add(Arg.Is<Project>(x => x.Location == expected));
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    [InlineData("missing@example.test", false, false)]
    [InlineData("owner@example.test", true, true)]
    public async Task UnavailableOwner_FailsWithoutCreatingAnything(string? email, bool exists, bool inactive)
    {
        var submission = Submission();
        var identities = Substitute.For<IIdentityRepository>();
        if (exists)
        {
            var owner = Owner();
            if (inactive) owner.Deactivate(DateTimeOffset.UtcNow);
            identities.FindUserByNormalizedEmailForUpdateAsync(Arg.Any<string>(), Token).Returns(owner);
        }
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var result = await Service(new(submission), identities, clients, projects, email)
            .ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("FAILED", result!.Status);
        Assert.Equal("PUBLIC_EXPERIENCE_OWNER_NOT_AVAILABLE", result.ErrorCode);
        Assert.Null(result.ClientId);
        Assert.Null(result.ProjectId);
        Assert.Empty(clients.ReceivedCalls());
        Assert.Empty(projects.ReceivedCalls());
    }

    [Fact]
    public async Task LongLocation_FailsWithoutTruncatingOrCreating()
    {
        var submission = Submission(subdivision: new string('x', 250));
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var result = await Service(new(submission), Identities(), clients, projects)
            .ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("FAILED", result!.Status);
        Assert.Equal("PROJECT_LOCATION_INVALID", result.ErrorCode);
        Assert.Empty(clients.ReceivedCalls());
        Assert.Empty(projects.ReceivedCalls());
        Assert.Equal(250, submission.ProjectSubdivision!.Length);
    }

    [Fact]
    public async Task ProcessedReplay_ReturnsSamePair_EvenIfOwnerConfigurationChanges()
    {
        var submission = Submission();
        var repository = new ProcessingMemoryRepository(submission);
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var first = await Service(repository, Identities(), clients, projects).ExecuteAsync(submission.SubmissionId, Token);
        var replay = await Service(repository, Substitute.For<IIdentityRepository>(), clients, projects, null)
            .ExecuteAsync(submission.SubmissionId, Token);
        Assert.True(replay!.IsReplay);
        Assert.Equal(first!.ClientId, replay.ClientId);
        Assert.Equal(first.ProjectId, replay.ProjectId);
        Assert.Single(clients.ReceivedCalls());
        Assert.Single(projects.ReceivedCalls());
    }

    [Fact]
    public async Task FailedOwner_CanRetryAfterConfigurationIsFixed()
    {
        var submission = Submission();
        var repository = new ProcessingMemoryRepository(submission);
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        await Service(repository, Identities(), clients, projects, null).ExecuteAsync(submission.SubmissionId, Token);
        var result = await Service(repository, Identities(), clients, projects).ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal("PROCESSED", result!.Status);
        Assert.Null(result.ErrorCode);
        Assert.Single(clients.ReceivedCalls());
    }

    [Fact]
    public async Task DifferentSubmissions_CreateDifferentClients_NoPhoneDeduplication()
    {
        var clients = Substitute.For<IClientRepository>();
        var projects = Substitute.For<IProjectRepository>();
        foreach (var submission in new[] { Submission(), Submission() })
            await Service(new(submission), Identities(), clients, projects).ExecuteAsync(submission.SubmissionId, Token);
        var created = clients.ReceivedCalls().Select(call => (Client)call.GetArguments()[0]!).ToArray();
        Assert.Equal(2, created.Length);
        Assert.NotEqual(created[0].Id, created[1].Id);
        Assert.Equal(created[0].Phone, created[1].Phone);
    }

    public static PublicExperienceSubmission Submission(string advisor = "DIRECT_CONTACT",
        string? subdivision = null, string? lot = null, bool withFile = true)
    {
        var submission = new PublicExperienceSubmission
        {
        SubmissionId = Guid.NewGuid().ToString(), SchemaVersion = "sng-public-experience-v1", Source = "SNG_WEB",
        ClientFullName = "Example", ClientWhatsapp = "+5700", AdvisorCode = advisor, AdvisorDisplayName = "Public advisor",
        ProjectName = "Project", ProjectCity = "Bucaramanga", ProjectSubdivision = subdivision, ProjectLotOrApartment = lot,
        ProjectNotes = "Project notes", AnswersJson = "{}", PayloadFingerprint = new string('A', 64),
        CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        if (withFile) submission.Files.Add(new()
        {
            SubmissionId = submission.Id, Sequence = 0, OriginalFileName = "original.pdf",
            ContentType = "application/pdf", SizeBytes = 4,
            StorageKey = $"public-experience/{submission.Id:N}/original.pdf", ContentHash = new string('A', 64)
        });
        return submission;
    }

    public static User Owner() => User.CreateFromGoogle("owner@example.test", "Owner", null, null, DateTimeOffset.UtcNow);

    private static IIdentityRepository Identities()
    {
        var identities = Substitute.For<IIdentityRepository>();
        identities.FindUserByNormalizedEmailForUpdateAsync("owner@example.test", Token).Returns(Owner());
        return identities;
    }

    private static ProcessPublicExperienceSubmissionService Service(ProcessingMemoryRepository repository,
        IIdentityRepository identities, IClientRepository clients, IProjectRepository projects,
        string? email = "owner@example.test") =>
        new(repository, identities, clients, projects, Options.Create(new PublicExperienceOptions { OwnerEmail = email }), TimeProvider.System);
}

internal sealed class ProcessingMemoryRepository(PublicExperienceSubmission submission) : IPublicExperienceProcessingRepository
{
    public async Task<PublicExperienceProcessingResult?> ProcessLockedAsync(string id,
        Func<PublicExperienceSubmission, CancellationToken, Task> process, CancellationToken cancellationToken)
    {
        if (id != submission.SubmissionId) return null;
        var replay = submission.Status == "PROCESSED";
        if (!replay) await process(submission, cancellationToken);
        return new(id, submission.Status, submission.ClientId, submission.ProjectId, submission.ProcessingErrorCode, replay);
    }
}
