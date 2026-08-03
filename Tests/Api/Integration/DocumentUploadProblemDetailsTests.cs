using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Api.ErrorHandling;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreatePreQuoteDocument;
using Application.PreQuotes.GetPreQuoteDocuments;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.Clients;
using Domain.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class DocumentUploadProblemDetailsTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("unauthorized", 401, PreQuoteErrorCodes.Unauthorized)]
    [InlineData("inactive_user", 403, PreQuoteErrorCodes.InactiveUser)]
    [InlineData("unsupported", 415, DocumentErrorCodes.UnsupportedFileType)]
    [InlineData("empty", 422, DocumentErrorCodes.EmptyFile)]
    [InlineData("too_large", 413, DocumentErrorCodes.FileTooLarge)]
    [InlineData("not_found", 404, DocumentErrorCodes.PreQuoteNotFound)]
    [InlineData("foreign", 404, DocumentErrorCodes.PreQuoteNotFound)]
    [InlineData("inactive_project", 409, DocumentErrorCodes.ProjectInactive)]
    [InlineData("inactive_client", 409, DocumentErrorCodes.ClientInactive)]
    [InlineData("storage", 500, DocumentErrorCodes.StorageError)]
    [InlineData("persistence", 500, DocumentErrorCodes.PersistenceError)]
    public async Task Post_Failure_ReturnsStableProblem(
        string scenario, int status, string errorCode)
    {
        await using var host = await ControlledHost.StartAsync(scenario);
        using var content = CreateMultipart(scenario);
        using var response = await host.Client.PostAsync(
            $"/api/v1/prequotes/{host.PreQuoteId}/documents", content,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, status, errorCode);
        if (scenario == "persistence")
        {
            await host.Storage.Received(1).DeleteIfExistsAsync(
                Arg.Any<string>(), CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("invalid_uuid")]
    [InlineData("not_multipart")]
    [InlineData("missing_file")]
    [InlineData("wrong_field")]
    public async Task Post_InvalidMultipart_ReturnsDocumentInvalidRequest(
        string scenario)
    {
        await using var host = await ControlledHost.StartAsync("success");
        using var content = scenario == "not_multipart"
            ? new StringContent("invalid")
            : CreateMultipart(scenario);
        var id = scenario == "invalid_uuid" ? "not-a-uuid" : host.PreQuoteId.ToString();
        using var response = await host.Client.PostAsync(
            $"/api/v1/prequotes/{id}/documents", content,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, 400, DocumentErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Post_ExactMaximum_PreservesCreatedContract()
    {
        await using var host = await ControlledHost.StartAsync("success");
        using var content = CreateMultipart("maximum");
        using var response = await host.Client.PostAsync(
            $"/api/v1/prequotes/{host.PreQuoteId}/documents", content,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatePreQuoteDocumentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(CreatePreQuoteDocumentService.MaximumFileSizeBytes, body.SizeBytes);
        await host.Repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_DocumentsMultipartAndStableErrors()
    {
        var method = typeof(PreQuoteDocumentsController).GetMethod(
            nameof(PreQuoteDocumentsController.Create));
        Assert.NotNull(method);
        var responses = method.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>().ToArray();
        foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 415, 422, 500 })
            Assert.Contains(responses, value => value.StatusCode == status
                && value.Type == typeof(ApiProblemDetailsResponse));
        var fileParameter = method.GetParameters()[1];
        Assert.Equal("file", fileParameter.Name);
        Assert.Equal(typeof(IFormFile), fileParameter.ParameterType);
    }

    private static HttpContent CreateMultipart(string scenario)
    {
        var form = new MultipartFormDataContent();
        if (scenario == "missing_file") return form;
        var length = scenario switch
        {
            "empty" => 0,
            "maximum" => (int)CreatePreQuoteDocumentService.MaximumFileSizeBytes,
            "too_large" => (int)CreatePreQuoteDocumentService.MaximumFileSizeBytes + 1,
            _ => 4
        };
        var file = new ByteArrayContent(new byte[length]);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            scenario == "unsupported" ? "text/plain" : "application/pdf");
        form.Add(file, scenario == "wrong_field" ? "document" : "file", "document.pdf");
        return form;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, int status, string errorCode)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        var contract = await response.Content.ReadFromJsonAsync<
            ApiProblemDetailsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(contract);
        Assert.Equal(errorCode, contract.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(contract.TraceId));
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(errorCode, root.GetProperty("errorCode").GetString());
        Assert.False(root.TryGetProperty("code", out _));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication application;
        private ControlledHost(WebApplication app, HttpClient client, Guid id,
            IPreQuoteRepository repository, IFileStorage storage)
        { application = app; Client = client; PreQuoteId = id; Repository = repository; Storage = storage; }
        public HttpClient Client { get; }
        public Guid PreQuoteId { get; }
        public IPreQuoteRepository Repository { get; }
        public IFileStorage Storage { get; }

        public static async Task<ControlledHost> StartAsync(string scenario)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(PreQuoteDocumentsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.Logging.AddConsole();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var current = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var preQuotes = Substitute.For<IPreQuoteRepository>();
            var projects = Substitute.For<IProjectRepository>();
            var clients = Substitute.For<IClientRepository>();
            var storage = Substitute.For<IFileStorage>();
            var query = Substitute.For<IPreQuoteDocumentQueryRepository>();
            var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
            var clientEntity = global::Domain.Clients.Client.Create(ClientType.Company, "Client", null, null, null, null, null, null, null, UserId, At);
            var owner = scenario == "foreign" ? Guid.NewGuid() : UserId;
            var project = ProjectEntity.Create(clientEntity.Id, "P-1", "Project", null, null, owner, At);
            var preQuote = global::Domain.PreQuotes.PreQuote.Create(
                project.Id, UserId, At);
            current.IsAuthenticated.Returns(scenario != "unauthorized"); current.UserId.Returns(UserId);
            if (scenario == "inactive_user") user.Deactivate(At.AddMinutes(1));
            if (scenario == "inactive_project") project.SetActive(false, owner, At.AddMinutes(1));
            if (scenario == "inactive_client") clientEntity.SetActive(false, UserId, At.AddMinutes(1));
            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
            preQuotes.FindForUpdateByIdAsync(preQuote.Id, Arg.Any<CancellationToken>()).Returns(scenario == "not_found" ? null : preQuote);
            projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
            clients.FindByIdAsync(clientEntity.Id, Arg.Any<CancellationToken>()).Returns(clientEntity);
            if (scenario == "storage") storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new FileStorageWriteException(new IOException("sensitive"))));
            preQuotes.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(scenario == "persistence"
                ? Task.FromException(new PreQuotePersistenceException(new InvalidOperationException("sensitive"))) : Task.CompletedTask);
            builder.Services.AddControllers().AddApplicationPart(typeof(PreQuoteDocumentsController).Assembly);
            builder.Services.AddPreQuoteProblemDetailsContract(); builder.Services.AddAuthorization(); builder.Services.AddLogging();
            builder.Services.AddSingleton(current); builder.Services.AddSingleton(identity); builder.Services.AddSingleton(preQuotes);
            builder.Services.AddSingleton(projects); builder.Services.AddSingleton(clients); builder.Services.AddSingleton(storage); builder.Services.AddSingleton(query);
            builder.Services.AddSingleton<IValidator<CreatePreQuoteDocumentCommand>, CreatePreQuoteDocumentCommandValidator>();
            builder.Services.AddSingleton<IValidator<GetPreQuoteDocumentsQuery>, GetPreQuoteDocumentsQueryValidator>();
            builder.Services.AddScoped<CreatePreQuoteDocumentService>(); builder.Services.AddScoped<GetPreQuoteDocumentsService>();
            var app = builder.Build(); app.UseRouting(); app.Use(async (context, next) =>
            { context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test")); await next(context); });
            app.UseAuthorization(); app.UseContractualProblemDetails();
            app.MapControllers(); await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ControlledHost(app, new HttpClient { BaseAddress = new Uri(address) }, preQuote.Id, preQuotes, storage);
        }

        public async ValueTask DisposeAsync()
        { Client.Dispose(); try { await application.StopAsync(); } finally { await application.DisposeAsync(); } }
    }
}
