using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Api.ErrorHandling;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.CreatePreQuote;
using Application.PreQuotes.GetProjectPreQuotes;
using Contracts.Common;
using Contracts.PreQuotes;
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
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class PreQuoteCreationProblemDetailsTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("empty", 400, PreQuoteErrorCodes.InvalidRequest)]
    [InlineData("unauthorized", 401, PreQuoteErrorCodes.Unauthorized)]
    [InlineData("inactive_user", 403, PreQuoteErrorCodes.InactiveUser)]
    [InlineData("project_not_found", 404, PreQuoteErrorCodes.ProjectNotFound)]
    [InlineData("foreign_project", 404, PreQuoteErrorCodes.ProjectNotFound)]
    [InlineData("inactive_project", 409, PreQuoteErrorCodes.ProjectInactive)]
    [InlineData("client_not_found", 404, PreQuoteErrorCodes.ClientNotFound)]
    [InlineData("inactive_client", 409, PreQuoteErrorCodes.ClientInactive)]
    [InlineData("query", 500, PreQuoteErrorCodes.QueryError)]
    [InlineData("persistence", 500, PreQuoteErrorCodes.PersistenceError)]
    public async Task Post_Failure_ReturnsStableProblemDetails(
        string scenario,
        int status,
        string errorCode)
    {
        await using var host = await ControlledHost.StartAsync(scenario);
        var projectId = scenario == "empty" ? Guid.Empty : host.ProjectId;
        using var response = await host.Client.PostAsync(
            $"/api/v1/projects/{projectId}/prequotes", null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, status, errorCode);
    }

    [Fact]
    public async Task Post_InvalidUuid_ReturnsContractualBadRequest()
    {
        await using var host = await ControlledHost.StartAsync("success");
        using var response = await host.Client.PostAsync(
            "/api/v1/projects/not-a-uuid/prequotes", null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            response, 400, PreQuoteErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Post_Success_PreservesCreatedResponse()
    {
        await using var host = await ControlledHost.StartAsync("success");
        using var response = await host.Client.PostAsync(
            $"/api/v1/projects/{host.ProjectId}/prequotes", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<
            CreatePreQuoteResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(host.ProjectId, body.ProjectId);
        await host.PreQuoteRepository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_DocumentsStableProblemDetailsSchema()
    {
        var method = typeof(ProjectPreQuotesController).GetMethod(
            nameof(ProjectPreQuotesController.Create));
        Assert.NotNull(method);
        var responses = method.GetCustomAttributes<
            ProducesResponseTypeAttribute>().ToArray();
        Assert.Contains(responses, response => response.StatusCode == 201
            && response.Type == typeof(CreatePreQuoteResponse));
        foreach (var status in new[] { 400, 401, 403, 404, 409, 500 })
        {
            Assert.Contains(responses, response => response.StatusCode == status
                && response.Type == typeof(ApiProblemDetailsResponse));
        }
        Assert.Equal(9, typeof(PreQuoteErrorCodes).GetFields(
            BindingFlags.Public | BindingFlags.Static).Length);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        int status,
        string errorCode)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.StartsWith("application/problem+json",
            response.Content.Headers.ContentType?.ToString());
        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("title").GetString()));
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("detail").GetString()));
        Assert.Equal(errorCode,
            root.GetProperty("errorCode").GetString());
        Assert.False(root.TryGetProperty("code", out _));
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("sensitive", raw,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private ControlledHost(
            WebApplication application,
            HttpClient client,
            Guid projectId,
            IPreQuoteRepository preQuoteRepository)
        {
            _application = application;
            Client = client;
            ProjectId = projectId;
            PreQuoteRepository = preQuoteRepository;
        }
        public HttpClient Client { get; }
        public Guid ProjectId { get; }
        public IPreQuoteRepository PreQuoteRepository { get; }

        public static async Task<ControlledHost> StartAsync(string scenario)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(ProjectPreQuotesController)
                        .Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var currentUser = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var projects = Substitute.For<IProjectRepository>();
            var clients = Substitute.For<IClientRepository>();
            var preQuotes = Substitute.For<IPreQuoteRepository>();
            var user = User.CreateFromGoogle(
                "user@example.com", "User", null, null, At);
            var client = global::Domain.Clients.Client.Create(
                ClientType.Company, "Client", null, null, null, null,
                null, null, null, UserId, At);
            var owner = scenario == "foreign_project"
                ? Guid.NewGuid()
                : UserId;
            var project = ProjectEntity.Create(
                client.Id, "PR-001", "Project", null, null, owner, At);
            currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
            currentUser.UserId.Returns(UserId);
            if (scenario == "inactive_user")
                user.Deactivate(At.AddMinutes(1));
            if (scenario == "inactive_project")
                project.SetActive(false, owner, At.AddMinutes(1));
            if (scenario == "inactive_client")
                client.SetActive(false, UserId, At.AddMinutes(1));
            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
                .Returns(user);
            projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
                .Returns(scenario == "project_not_found" ? null : project);
            clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
                .Returns(scenario == "client_not_found" ? null : client);
            if (scenario == "query")
            {
                projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<ProjectEntity?>(
                        new ProjectQueryException(
                            new InvalidOperationException("sensitive"))));
            }
            preQuotes.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(scenario == "persistence"
                    ? Task.FromException(new PreQuotePersistenceException(
                        new InvalidOperationException("sensitive")))
                    : Task.CompletedTask);
            builder.Services.AddControllers().AddApplicationPart(
                typeof(ProjectPreQuotesController).Assembly);
            builder.Services.AddPreQuoteProblemDetailsContract();
            builder.Services.AddAuthorization();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(projects);
            builder.Services.AddSingleton(clients);
            builder.Services.AddSingleton(preQuotes);
            builder.Services.AddSingleton<IValidator<CreatePreQuoteCommand>,
                CreatePreQuoteCommandValidator>();
            builder.Services.AddSingleton<IValidator<GetProjectPreQuotesQuery>,
                GetProjectPreQuotesQueryValidator>();
            builder.Services.AddScoped<CreatePreQuoteService>();
            builder.Services.AddScoped<GetProjectPreQuotesService>();
            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
                    "Test"));
                await next(context);
            });
            application.UseAuthorization();
            application.MapControllers();
            try
            {
                await application.StartAsync();
                var address = application.Services
                    .GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single();
                return new ControlledHost(
                    application,
                    new HttpClient { BaseAddress = new Uri(address) },
                    project.Id,
                    preQuotes);
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try { await _application.StopAsync(); }
            finally { await _application.DisposeAsync(); }
        }
    }
}
