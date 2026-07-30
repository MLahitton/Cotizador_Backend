using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Projects.CreateProject;
using Application.Projects.GetProjectById;
using Application.Projects.GetProjects;
using Application.Projects.SetProjectActivation;
using Application.Projects.UpdateProject;
using Contracts.Common;
using Contracts.Projects;
using Domain.Clients;
using Domain.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class ProjectCreationProblemDetailsTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(
        "invalid",
        400,
        "PROJECT_INVALID_REQUEST",
        "Solicitud inv\u00e1lida")]
    [InlineData(
        "unauthorized",
        401,
        "AUTH_UNAUTHORIZED",
        "No autorizado")]
    [InlineData(
        "inactive_user",
        403,
        "AUTH_USER_INACTIVE",
        "Usuario inactivo")]
    [InlineData(
        "client_not_found",
        404,
        "PROJECT_CLIENT_NOT_FOUND",
        "Cliente no encontrado")]
    [InlineData(
        "inactive_client",
        409,
        "PROJECT_CLIENT_INACTIVE",
        "Cliente inactivo")]
    [InlineData(
        "duplicate",
        409,
        "PROJECT_CODE_DUPLICATE",
        "C\u00f3digo de proyecto duplicado")]
    [InlineData(
        "persistence",
        500,
        "PROJECT_PERSISTENCE_ERROR",
        "Error al crear el proyecto")]
    public async Task Post_Failure_ReturnsStableProblemDetails(
        string scenario,
        int expectedStatus,
        string expectedCode,
        string expectedTitle)
    {
        await using var host = await ControlledHost.StartAsync(scenario);
        var request = new CreateProjectRequest(
            ClientId,
            scenario == "invalid" ? string.Empty : "PR-001",
            "Proyecto Uno",
            "Descripcion",
            "Bogota");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/v1/projects",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("type").ValueKind);
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("detail").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("detail").GetString()));
        Assert.Equal(JsonValueKind.String, root.GetProperty("code").ValueKind);
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(
            JsonValueKind.String,
            root.GetProperty("traceId").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("traceId").GetString()));
        foreach (var forbidden in new[]
        {
            "sql",
            "constraint",
            "exception",
            "ProjectPersistenceException",
            "IProjectRepository",
            ".cs:",
            "C:\\"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                raw,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Post_ValidRequest_PreservesSuccessfulCreation()
    {
        await using var host = await ControlledHost.StartAsync("success");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest(
                ClientId,
                "PR-001",
                "Proyecto Uno",
                "Descripcion",
                "Bogota"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<
            CreateProjectResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.ClientId);
        Assert.Equal("PR-001", body.Code);
        await host.ClientRepository.Received(1).FindByIdAsync(
            ClientId,
            Arg.Any<CancellationToken>());
        await host.ProjectRepository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_DocumentsStableProblemDetailsSchema()
    {
        var method = typeof(ProjectsController).GetMethod(
            nameof(ProjectsController.Create));
        Assert.NotNull(method);
        var responses = method.GetCustomAttributes<
            ProducesResponseTypeAttribute>().ToArray();

        foreach (var status in new[] { 400, 401, 403, 404, 409, 500 })
        {
            Assert.Contains(
                responses,
                response => response.StatusCode == status
                    && response.Type
                        == typeof(ApiProblemDetailsResponse));
        }
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private ControlledHost(
            WebApplication application,
            HttpClient client,
            IClientRepository clientRepository,
            IProjectRepository projectRepository)
        {
            Application = application;
            Client = client;
            ClientRepository = clientRepository;
            ProjectRepository = projectRepository;
        }

        public WebApplication Application { get; }
        public HttpClient Client { get; }
        public IClientRepository ClientRepository { get; }
        public IProjectRepository ProjectRepository { get; }

        public static async Task<ControlledHost> StartAsync(string scenario)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName =
                        typeof(ProjectsController).Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var currentUser = Substitute.For<ICurrentUser>();
            var identityRepository = Substitute.For<IIdentityRepository>();
            var clientRepository = Substitute.For<IClientRepository>();
            var projectRepository = Substitute.For<IProjectRepository>();
            var user = CreateUser();
            var client = CreateClient();
            currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
            currentUser.UserId.Returns(UserId);
            if (scenario == "inactive_user")
            {
                user.Deactivate(At.AddMinutes(1));
            }
            if (scenario == "inactive_client")
            {
                client.SetActive(false, UserId, At.AddMinutes(1));
            }
            identityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(user);
            clientRepository.FindByIdAsync(
                    ClientId,
                    Arg.Any<CancellationToken>())
                .Returns(
                    scenario == "client_not_found"
                        ? null
                        : client);
            projectRepository.ExistsByCodeAsync(
                    "PR-001",
                    Arg.Any<CancellationToken>())
                .Returns(scenario == "duplicate");
            projectRepository.SaveChangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns(
                    scenario == "persistence"
                        ? Task.FromException(
                            new ProjectPersistenceException(
                                new InvalidOperationException(
                                    "sensitive database detail")))
                        : Task.CompletedTask);

            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(ProjectsController).Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identityRepository);
            builder.Services.AddSingleton(clientRepository);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<
                IValidator<CreateProjectCommand>,
                CreateProjectCommandValidator>();
            builder.Services.AddSingleton<
                IValidator<GetProjectsQuery>,
                GetProjectsQueryValidator>();
            builder.Services.AddSingleton<
                IValidator<GetProjectByIdQuery>,
                GetProjectByIdQueryValidator>();
            builder.Services.AddSingleton<
                IValidator<UpdateProjectCommand>,
                UpdateProjectCommandValidator>();
            builder.Services.AddSingleton<
                IValidator<SetProjectActivationCommand>,
                SetProjectActivationCommandValidator>();
            builder.Services.AddScoped<CreateProjectService>();
            builder.Services.AddScoped<GetProjectsService>();
            builder.Services.AddScoped<GetProjectByIdService>();
            builder.Services.AddScoped<UpdateProjectService>();
            builder.Services.AddScoped<SetProjectActivationService>();

            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [new Claim(
                        ClaimTypes.NameIdentifier,
                        UserId.ToString())],
                    "Test"));
                await next(context);
            });
            application.UseAuthorization();
            application.MapControllers();
            var started = false;
            HttpClient? clientInstance = null;
            try
            {
                await application.StartAsync();
                started = true;
                var addresses = application.Services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses;
                Assert.NotNull(addresses);
                clientInstance = new HttpClient
                {
                    BaseAddress = new Uri(Assert.Single(addresses))
                };
                return new(
                    application,
                    clientInstance,
                    clientRepository,
                    projectRepository);
            }
            catch (Exception originalException)
            {
                try { clientInstance?.Dispose(); } catch { }
                try
                {
                    if (started)
                    {
                        await application.StopAsync(
                            TestContext.Current.CancellationToken);
                    }
                }
                catch { }
                finally
                {
                    try { await application.DisposeAsync(); } catch { }
                }
                ExceptionDispatchInfo.Capture(originalException).Throw();
                throw new InvalidOperationException("Unreachable.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
        }

        private static User CreateUser() => User.CreateFromGoogle(
            "admin@example.com",
            "Admin",
            "User",
            null,
            At);

        private static Client CreateClient() =>
            global::Domain.Clients.Client.Create(
            ClientType.Company,
            "Servicios Nacionales",
            "SnG",
            ClientDocumentType.Nit,
            "900.123.456-7",
            "contacto@sng.example",
            "6015550101",
            "Calle 1",
            "Bogota",
            UserId,
            At);
    }
}
