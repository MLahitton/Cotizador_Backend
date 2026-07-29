using Api.Controllers;
using Application.Clients.CreateClient;
using Application.Clients.GetClientById;
using Application.Clients.GetClients;
using Application.Clients.SetClientActivation;
using Application.Clients.UpdateClient;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Projects.CreateProject;
using Application.Projects.GetProjectById;
using Application.Projects.GetProjects;
using Application.Projects.SetProjectActivation;
using Application.Projects.UpdateProject;
using Domain.Clients;
using Domain.Identity;
using Domain.Projects;
using NSubstitute;

namespace CotizadorBackend.Tests.TestDoubles;

public sealed class AdministrationTestContext
{
    public static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public AdministrationTestContext()
    {
        CurrentUser = Substitute.For<ICurrentUser>();
        IdentityRepository = Substitute.For<IIdentityRepository>();
        ClientRepository = Substitute.For<IClientRepository>();
        ProjectRepository = Substitute.For<IProjectRepository>();
        User = User.CreateFromGoogle(
            "admin@example.com",
            "Admin",
            "User",
            null,
            CreatedAt);
        Client = Client.Create(
            ClientType.Company,
            "Servicios Nacionales",
            "SnG",
            ClientDocumentType.Nit,
            "900.123.456-7",
            "contacto@sng.example",
            "6015550101",
            "Calle 1",
            "Bogota",
            User.Id,
            CreatedAt);
        Project = Project.Create(
            Client.Id,
            "PR-001",
            "Proyecto Uno",
            "Descripcion",
            "Bogota",
            User.Id,
            CreatedAt);

        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.UserId.Returns(User.Id);
        IdentityRepository.FindUserByIdAsync(
                User.Id,
                Arg.Any<CancellationToken>())
            .Returns(User);
        ClientRepository.SearchAsync(
                Arg.Any<ClientSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClientSearchPage([], 0));
        ProjectRepository.SearchAsync(
                Arg.Any<ProjectSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdministrativeProjectSearchPage([], 0));
        ProjectRepository.FindForUpdateByIdAsync(
                Project.Id,
                Arg.Any<CancellationToken>())
            .Returns(Project);

        GetClientsService = new GetClientsService(
            new GetClientsQueryValidator(),
            CurrentUser,
            IdentityRepository,
            ClientRepository);
        GetProjectsService = new GetProjectsService(
            new GetProjectsQueryValidator(),
            CurrentUser,
            IdentityRepository,
            ProjectRepository);
        SetProjectActivationService = new SetProjectActivationService(
            new SetProjectActivationCommandValidator(),
            CurrentUser,
            IdentityRepository,
            ProjectRepository);

        ClientsController = new ClientsController(
            new CreateClientService(
                new CreateClientCommandValidator(),
                CurrentUser,
                IdentityRepository,
                ClientRepository),
            GetClientsService,
            new GetClientByIdService(
                new GetClientByIdQueryValidator(),
                CurrentUser,
                IdentityRepository,
                ClientRepository),
            new UpdateClientService(
                new UpdateClientCommandValidator(),
                CurrentUser,
                IdentityRepository,
                ClientRepository),
            new SetClientActivationService(
                new SetClientActivationCommandValidator(),
                CurrentUser,
                IdentityRepository,
                ClientRepository));

        ProjectsController = new ProjectsController(
            new CreateProjectService(
                new CreateProjectCommandValidator(),
                CurrentUser,
                IdentityRepository,
                ClientRepository,
                ProjectRepository),
            GetProjectsService,
            new GetProjectByIdService(
                new GetProjectByIdQueryValidator(),
                CurrentUser,
                IdentityRepository,
                ProjectRepository),
            new UpdateProjectService(
                new UpdateProjectCommandValidator(),
                CurrentUser,
                IdentityRepository,
                ProjectRepository),
            SetProjectActivationService);
    }

    public ICurrentUser CurrentUser { get; }
    public IIdentityRepository IdentityRepository { get; }
    public IClientRepository ClientRepository { get; }
    public IProjectRepository ProjectRepository { get; }
    public User User { get; }
    public Client Client { get; }
    public Project Project { get; }
    public GetClientsService GetClientsService { get; }
    public GetProjectsService GetProjectsService { get; }
    public SetProjectActivationService SetProjectActivationService { get; }
    public ClientsController ClientsController { get; }
    public ProjectsController ProjectsController { get; }

    public void SetInactiveUser()
    {
        User.Deactivate(CreatedAt.AddMinutes(1));
    }
}
