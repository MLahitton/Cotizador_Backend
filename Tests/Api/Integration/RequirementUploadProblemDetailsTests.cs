using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Api.ErrorHandling;
using Api.OpenApi;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
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

public sealed class RequirementUploadProblemDetailsTests
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
    public async Task OpenApi_RequirementUpload_UsesMultipartBinaryFilesOnly()
    {
        await using var host = await ControlledHost.StartAsync("success");

        using var response = await host.Client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(
                TestContext.Current.CancellationToken));
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v2/prequotes/{preQuoteId}/requirements")
            .GetProperty("post");
        var content = operation.GetProperty("requestBody")
            .GetProperty("content");

        Assert.True(content.TryGetProperty("multipart/form-data", out var multipart));
        Assert.False(content.TryGetProperty(
            "application/x-www-form-urlencoded",
            out _));
        var schema = ResolveSchema(
            document.RootElement,
            multipart.GetProperty("schema"));
        Assert.True(schema.TryGetProperty("properties", out var properties));
        var files = properties.GetProperty("files");
        Assert.Equal("array", files.GetProperty("type").GetString());
        var fileItem = ResolveSchema(
            document.RootElement,
            files.GetProperty("items"));
        Assert.Equal("string", fileItem.GetProperty("type").GetString());
        Assert.Equal("binary", fileItem.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Post_WithMultipleFiles_ReturnsCreatedRequirement()
    {
        await using var host = await ControlledHost.StartAsync("success");
        using var content = new MultipartFormDataContent();
        AddFile(content, "requirement.pdf", PdfContentType);
        AddFile(content, "photo.jpg", JpegContentType);
        AddFile(content, "schedule.xlsx", XlsxContentType);

        using var response = await host.Client.PostAsync(
            $"/api/v2/prequotes/{host.PreQuoteId}/requirements",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<CreateRequirementResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(host.PreQuoteId, body!.PreQuoteId);
        Assert.Equal(3, body.FileCount);
        Assert.Equal("PENDING", body.Status);
        await host.Storage.Received(3).SaveAsync(
            Arg.Is<string>(key =>
                key.StartsWith(
                    $"requirements/{body.RequirementId:D}/",
                    StringComparison.Ordinal)),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unauthorized", 401, PreQuoteErrorCodes.Unauthorized)]
    [InlineData("inactive_user", 403, PreQuoteErrorCodes.InactiveUser)]
    [InlineData("not_found", 404, RequirementErrorCodes.PreQuoteNotFound)]
    [InlineData("inactive_project", 409, RequirementErrorCodes.ProjectInactive)]
    [InlineData("inactive_client", 409, RequirementErrorCodes.ClientInactive)]
    [InlineData("unsupported", 415, RequirementErrorCodes.UnsupportedFileType)]
    [InlineData("empty", 422, RequirementErrorCodes.EmptyFile)]
    [InlineData("storage", 500, RequirementErrorCodes.StorageError)]
    [InlineData("persistence", 500, RequirementErrorCodes.PersistenceError)]
    public async Task Post_Failure_ReturnsStableProblem(
        string scenario,
        int status,
        string errorCode)
    {
        await using var host = await ControlledHost.StartAsync(scenario);
        using var content = CreateMultipart(scenario);

        using var response = await host.Client.PostAsync(
            $"/api/v2/prequotes/{host.PreQuoteId}/requirements",
            content,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, status, errorCode);
    }

    [Theory]
    [InlineData("not_multipart", 415, ApiErrorCodes.ApiUnsupportedMediaType)]
    [InlineData("missing_files", 400, RequirementErrorCodes.InvalidRequest)]
    [InlineData("wrong_field", 400, RequirementErrorCodes.InvalidRequest)]
    public async Task Post_InvalidMultipart_ReturnsStableProblem(
        string scenario,
        int status,
        string errorCode)
    {
        await using var host = await ControlledHost.StartAsync("success");
        using HttpContent content = scenario == "not_multipart"
            ? new StringContent("invalid")
            : CreateMultipart(scenario);

        using var response = await host.Client.PostAsync(
            $"/api/v2/prequotes/{host.PreQuoteId}/requirements",
            content,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, status, errorCode);
    }

    [Fact]
    public void Create_DocumentsMultipartAndStableErrors()
    {
        var method = typeof(PreQuoteRequirementsController).GetMethod(
            nameof(PreQuoteRequirementsController.Create));
        Assert.NotNull(method);
        Assert.Equal(
            typeof(CreateRequirementForm),
            method.GetParameters()[1].ParameterType);
        Assert.NotNull(method.GetParameters()[1].GetCustomAttributes(
                typeof(FromFormAttribute),
                true)
            .SingleOrDefault());
    }

    private static MultipartFormDataContent CreateMultipart(string scenario)
    {
        var content = new MultipartFormDataContent();
        if (scenario == "missing_files")
        {
            return content;
        }

        var fileName = scenario == "unsupported" ? "file.txt" : "file.pdf";
        var contentType = scenario == "unsupported" ? "text/plain" : PdfContentType;
        var length = scenario == "empty" ? 0 : 4;
        AddFile(
            content,
            fileName,
            contentType,
            length,
            scenario == "wrong_field" ? "file" : "files");
        return content;
    }

    private static void AddFile(
        MultipartFormDataContent form,
        string fileName,
        string contentType,
        int length = 4,
        string fieldName = "files")
    {
        var file = new ByteArrayContent(new byte[length]);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, fieldName, fileName);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        int status,
        string errorCode)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString());
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var contract = await response.Content.ReadFromJsonAsync<
            ApiProblemDetailsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(contract);
        Assert.Equal(errorCode, contract!.ErrorCode);
        Assert.Equal(errorCode, json.RootElement.GetProperty("errorCode").GetString());
    }

    private static JsonElement ResolveSchema(
        JsonElement document,
        JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/').Last();
        return document.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication application;

        private ControlledHost(
            WebApplication application,
            HttpClient client,
            Guid preQuoteId,
            IFileStorage storage)
        {
            this.application = application;
            Client = client;
            PreQuoteId = preQuoteId;
            Storage = storage;
        }

        public HttpClient Client { get; }
        public Guid PreQuoteId { get; }
        public IFileStorage Storage { get; }

        public static async Task<ControlledHost> StartAsync(string scenario)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(PreQuoteRequirementsController)
                        .Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var current = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var preQuotes = Substitute.For<IPreQuoteRepository>();
            var projects = Substitute.For<IProjectRepository>();
            var clients = Substitute.For<IClientRepository>();
            var requirements = Substitute.For<IRequirementRepository>();
            var storage = Substitute.For<IFileStorage>();
            var user = User.CreateFromGoogle(
                "user@example.com",
                "User",
                null,
                null,
                At);
            var client = global::Domain.Clients.Client.Create(
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
            var preQuote = PreQuote.Create(project.Id, UserId, At);

            current.IsAuthenticated.Returns(scenario != "unauthorized");
            current.UserId.Returns(UserId);
            if (scenario == "inactive_user")
            {
                user.Deactivate(At.AddMinutes(1));
            }
            if (scenario == "inactive_project")
            {
                project.SetActive(false, UserId, At.AddMinutes(1));
            }
            if (scenario == "inactive_client")
            {
                client.SetActive(false, UserId, At.AddMinutes(1));
            }

            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
                .Returns(user);
            preQuotes.FindForUpdateByIdAsync(
                    preQuote.Id,
                    Arg.Any<CancellationToken>())
                .Returns(scenario == "not_found" ? null : preQuote);
            projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
                .Returns(project);
            clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
                .Returns(client);
            storage.SaveAsync(
                    Arg.Any<string>(),
                    Arg.Any<Stream>(),
                    Arg.Any<CancellationToken>())
                .Returns(scenario == "storage"
                    ? Task.FromException(new FileStorageWriteException(
                        new IOException("sensitive")))
                    : Task.CompletedTask);
            requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(scenario == "persistence"
                    ? Task.FromException(new RequirementPersistenceException(
                        new InvalidOperationException("sensitive")))
                    : Task.CompletedTask);

            builder.Services.AddControllers()
                .AddApplicationPart(
                    typeof(PreQuoteRequirementsController).Assembly);
            builder.Services.AddOpenApi(options =>
                options.AddOperationTransformer<
                    RequirementUploadMultipartOperationTransformer>());
            builder.Services.AddPreQuoteProblemDetailsContract();
            builder.Services.AddAuthorization();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(current);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(preQuotes);
            builder.Services.AddSingleton(projects);
            builder.Services.AddSingleton(clients);
            builder.Services.AddSingleton(requirements);
            builder.Services.AddSingleton(storage);
            builder.Services.AddSingleton<TimeProvider>(
                new FixedTimeProvider(At));
            builder.Services.AddSingleton<
                IValidator<CreateRequirementCommand>,
                CreateRequirementCommandValidator>();
            builder.Services.AddScoped<CreateRequirementService>();

            var app = builder.Build();
            app.UseRouting();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
                    "Test"));
                await next(context);
            });
            app.UseAuthorization();
            app.UseContractualProblemDetails();
            app.MapOpenApi();
            app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ControlledHost(
                app,
                new HttpClient { BaseAddress = new Uri(address) },
                preQuote.Id,
                storage);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await application.StopAsync();
            }
            finally
            {
                await application.DisposeAsync();
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
