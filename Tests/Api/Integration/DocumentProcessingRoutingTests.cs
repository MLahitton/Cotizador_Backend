using System.Net;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Api.ErrorHandling;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Contracts.PreQuotes;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using FluentValidation.Results;
using Infrastructure.DocumentProcessing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class DocumentProcessingRoutingTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Post_ThroughRealRouting_ReturnsResolvableLocation()
    {
        await using var host = await ControlledHost.StartAsync();

        using var postResponse = await host.Client.PostAsync(
            $"/api/v1/prequote-documents/{DocumentId}/processing-attempts",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);
        Assert.NotNull(postResponse.Headers.Location);
        var location = ResolveLocation(
            host.Client.BaseAddress!,
            postResponse.Headers.Location);
        var body = await postResponse.Content
            .ReadFromJsonAsync<DocumentProcessingAttemptStatusResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        var expectedSegments = new[]
        {
            "api",
            "v1",
            "prequote-documents",
            DocumentId.ToString(),
            "processing-attempts",
            body.ProcessingAttemptId.ToString()
        };
        Assert.Equal(
            expectedSegments,
            location.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.NotEqual(
            body.ProcessingAttemptId.ToString(),
            expectedSegments[3]);
        Assert.Equal(DocumentId, body.DocumentId);
        Assert.Equal("PENDING", body.ProcessingState);
        Assert.Null(body.Outcome);
        Assert.Null(body.Result);

        using var getResponse = await host.Client.GetAsync(
            location,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content
            .ReadFromJsonAsync<DocumentProcessingAttemptStatusResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(getBody);
        Assert.Equal(body.ProcessingAttemptId, getBody.ProcessingAttemptId);
        Assert.Equal(body.DocumentId, getBody.DocumentId);
        Assert.Equal("PENDING", getBody.ProcessingState);
        Assert.Equal(
            "false",
            host.Application.Configuration[
                "DocumentProcessingWorker:Enabled"]);
        Assert.DoesNotContain(
            host.Application.Services.GetServices<IHostedService>(),
            service => service is DocumentProcessingWorker);
        await host.Repository.DidNotReceive()
            .ClaimNextPendingDocumentProcessingAttemptAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_WithMalformedDocumentId_ReturnsStableInvalidRequest()
    {
        await using var host = await ControlledHost.StartAsync();

        using var response = await host.Client.PostAsync(
            "/api/v1/prequote-documents/not-a-guid/processing-attempts",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString());
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            DocumentProcessingErrorCodes.InvalidRequest,
            json.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            json.RootElement.GetProperty("traceId").GetString()));
    }

    private static Uri ResolveLocation(Uri baseAddress, Uri location)
    {
        return location.IsAbsoluteUri
            ? location
            : new Uri(baseAddress, location);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private ControlledHost(
            WebApplication application,
            HttpClient client,
            IDocumentProcessingRepository repository)
        {
            Application = application;
            Client = client;
            Repository = repository;
        }

        public WebApplication Application { get; }
        public HttpClient Client { get; }
        public IDocumentProcessingRepository Repository { get; }

        public static async Task<ControlledHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName =
                        typeof(DocumentProcessingAttemptsController)
                            .Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DocumentProcessingWorker:Enabled"] = "false",
                    ["DocumentProcessingWorker:PollInterval"] = "00:00:01"
                });

            var validator = Substitute.For<
                IValidator<CreateDocumentProcessingAttemptCommand>>();
            var currentUser = Substitute.For<ICurrentUser>();
            var identityRepository = Substitute.For<IIdentityRepository>();
            var repository =
                Substitute.For<IDocumentProcessingRepository>();
            DocumentProcessingAttempt? capturedAttempt = null;

            validator.ValidateAsync(
                    Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(UserId);
            identityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateUser());
            repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateSource());
            repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(false);
            repository.When(candidate => candidate.AddAttempt(
                    Arg.Any<DocumentProcessingAttempt>()))
                .Do(call => capturedAttempt =
                    call.Arg<DocumentProcessingAttempt>());
            repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            repository.FindAttemptStatusAsync(
                    DocumentId,
                    Arg.Any<Guid>(),
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var requestedAttemptId = call.ArgAt<Guid>(1);

                    if (capturedAttempt is null
                        || capturedAttempt.Id != requestedAttemptId)
                    {
                        return null;
                    }

                    return new DocumentProcessingAttemptStatusSnapshot(
                        capturedAttempt.Id,
                        capturedAttempt.PreQuoteDocumentId,
                        capturedAttempt.ProcessingState,
                        capturedAttempt.Outcome,
                        capturedAttempt.ErrorCode,
                        capturedAttempt.CreatedAtUtc,
                        capturedAttempt.StartedAtUtc,
                        capturedAttempt.CompletedAtUtc,
                        null);
                });

            builder.Services
                .AddControllers()
                .AddApplicationPart(
                    typeof(DocumentProcessingAttemptsController).Assembly);
            builder.Services.AddPreQuoteProblemDetailsContract();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(validator);
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identityRepository);
            builder.Services.AddSingleton(repository);
            builder.Services.AddSingleton<TimeProvider>(
                new FixedTimeProvider(CreatedAt));
            builder.Services
                .AddScoped<CreateDocumentProcessingAttemptService>();
            builder.Services
                .AddScoped<GetDocumentProcessingAttemptService>();

            var application = builder.Build();
            application.UseRouting();
            application.Use(
                async (context, next) =>
                {
                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                        [
                            new Claim(
                                ClaimTypes.NameIdentifier,
                                UserId.ToString())
                        ],
                        "Test"));
                    await next(context);
                });
            application.UseAuthorization();
            application.UseContractualProblemDetails();
            application.MapControllers();

            var applicationStarted = false;
            HttpClient? client = null;

            try
            {
                await application.StartAsync();
                applicationStarted = true;
                var addresses = application.Services
                    .GetRequiredService<IServer>()
                    .Features
                    .Get<IServerAddressesFeature>()
                    ?.Addresses;
                Assert.NotNull(addresses);
                var address = Assert.Single(addresses);
                client = new HttpClient
                {
                    BaseAddress = new Uri(address)
                };

                return new ControlledHost(
                    application,
                    client,
                    repository);
            }
            catch (Exception originalException)
            {
                try
                {
                    client?.Dispose();
                }
                catch
                {
                    // Preserve the host-construction exception.
                }

                try
                {
                    if (applicationStarted)
                    {
                        await application.StopAsync(
                            TestContext.Current.CancellationToken);
                    }
                }
                catch
                {
                    // Preserve the host-construction exception.
                }
                finally
                {
                    try
                    {
                        await application.DisposeAsync();
                    }
                    catch
                    {
                        // Preserve the host-construction exception.
                    }
                }

                ExceptionDispatchInfo
                    .Capture(originalException)
                    .Throw();
                throw new InvalidOperationException(
                    "Unreachable host cleanup path.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
        }

        private static User CreateUser()
        {
            return User.CreateFromGoogle(
                "user@example.com",
                "Test",
                "User",
                null,
                CreatedAt);
        }

        private static DocumentProcessingSource CreateSource()
        {
            return new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "document.pdf",
                "application/pdf",
                100,
                "prequotes/document.pdf",
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                UserId,
                true,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                true);
        }
    }
}
