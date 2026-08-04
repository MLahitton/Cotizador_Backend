using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Controllers;
using Api.ErrorHandling;
using Contracts.Common;
using Domain.Identity;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class BeErrAuditContractTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string JwtIssuer = "cotizador-test-issuer";
    private const string JwtAudience = "cotizador-test-audience";
    private const string JwtSigningKey =
        "cotizador-backend-test-signing-key-v1-minimum-32";

    [Fact]
    public async Task Get_WhenUnexpectedFailureOccurs_ReturnsGlobalProblem_500()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: false,
            withControllers: false,
            enforcePayloadLimit: false);

        using var response = await host.Client.GetAsync(
            "/api/v1/be-err-audit/unexpected-failure",
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.InternalServerError,
            ApiErrorCodes.InternalServerError,
            "application/problem+json");
        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("invalid", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_KnownFailure_PreservesAuthUnauthorized()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: true,
            withControllers: false,
            enforcePayloadLimit: false);

        using var response = await host.Client.GetAsync(
            "/api/v1/be-err-audit/protected",
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            ApiErrorCodes.AuthUnauthorized,
            "application/problem+json");
    }

    [Fact]
    public async Task Delete_ToProjects_ReturnsApiMethodNotAllowed()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: false,
            withControllers: false,
            enforcePayloadLimit: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/projects");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            ApiErrorCodes.ApiMethodNotAllowed,
            "application/problem+json");
    }

    [Fact]
    public async Task Post_ToProjects_WithLargeJsonBody_ReturnsApiPayloadTooLarge()
    {
        var bigBody = new string('x', 16_384);
        await using var host = await ControlledHost.StartAsync(
            withJwt: false,
            withControllers: false,
            enforcePayloadLimit: true);

        using var response = await host.Client.PostAsync(
            "/api/v1/projects",
            new StringContent(bigBody, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;

        Assert.True(host.PayloadLimiterProducedStatusOnly);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(raw));
        Assert.Equal("API_PAYLOAD_TOO_LARGE", root.GetProperty("errorCode").GetString());
        Assert.Equal((int)HttpStatusCode.RequestEntityTooLarge, root.GetProperty("status").GetInt32());
        Assert.Equal(
            "urn:cotizador:error:api_payload_too_large",
            root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.False(root.TryGetProperty("code", out _));
        Assert.False(root.TryGetProperty("stackTrace", out _));
        Assert.DoesNotContain(
            "stack",
            raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "internal",
            raw,
            StringComparison.OrdinalIgnoreCase);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            ApiErrorCodes.ApiPayloadTooLarge,
            "application/problem+json");
    }

    [Fact]
    public async Task Get_ToBadRequestException_TransformsToApiPayloadTooLarge()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: false,
            withControllers: false,
            enforcePayloadLimit: false);

        using var response = await host.Client.GetAsync(
            "/api/v1/projects/bad-request",
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            ApiErrorCodes.ApiPayloadTooLarge,
            "application/problem+json");

        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.Equal(
            "urn:cotizador:error:api_payload_too_large",
            root.GetProperty("type").GetString());
        Assert.Equal(
            (int)HttpStatusCode.RequestEntityTooLarge,
            root.GetProperty("status").GetInt32());
        Assert.Equal(
            ApiErrorCodes.ApiPayloadTooLarge,
            root.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.False(root.TryGetProperty("stack", out _));
        Assert.DoesNotContain(
            "stack",
            raw,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_WithInvalidJwt_ReturnsAuthUnauthorized()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: true,
            withControllers: false,
            enforcePayloadLimit: false);

        var token = CreateJwtToken(
            JwtSigningKey,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            UserId);
        var badToken = CreateJwtToken(
            "totally-invalid-signing-key-for-test-9999",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            UserId);

        using var badTokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/be-err-audit/protected");
        badTokenRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                badToken);
        using var badTokenResponse = await host.Client.SendAsync(
            badTokenRequest,
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            badTokenResponse,
            HttpStatusCode.Unauthorized,
            ApiErrorCodes.AuthUnauthorized,
            "application/problem+json");

        var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/be-err-audit/protected");
        tokenRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);
        using var validTokenResponse = await host.Client.SendAsync(
            tokenRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validTokenResponse.StatusCode);
    }

    [Fact]
    public async Task Get_WithExpiredJwt_ReturnsAuthUnauthorized()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: true,
            withControllers: false,
            enforcePayloadLimit: false);

        var expiredToken = CreateJwtToken(
            JwtSigningKey,
            DateTimeOffset.UtcNow.AddMinutes(-15),
            UserId,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/be-err-audit/protected");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                expiredToken);
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            ApiErrorCodes.AuthUnauthorized,
            "application/problem+json");
    }

    [Fact]
    public async Task Get_WithAuthorizationPolicyDenied_ReturnsAuthForbidden()
    {
        await using var host = await ControlledHost.StartAsync(
            withJwt: true,
            withControllers: false,
            enforcePayloadLimit: false);

        var token = CreateJwtToken(
            JwtSigningKey,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            UserId);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/be-err-audit/forbidden-policy");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemResponseAsync(
            response,
            HttpStatusCode.Forbidden,
            ApiErrorCodes.AuthForbidden,
            "application/problem+json");
    }

    private static async Task AssertProblemResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string expectedContentType)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.ToString();
        Assert.StartsWith(expectedContentType, contentType);

        var contract = await response.Content.ReadFromJsonAsync<ApiProblemDetailsResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(contract);
        Assert.Equal(expectedCode, contract.ErrorCode);
        Assert.Equal((int)expectedStatus, contract.Status);
        Assert.False(string.IsNullOrWhiteSpace(contract.Type));
        Assert.False(string.IsNullOrWhiteSpace(contract.Title));
        Assert.False(string.IsNullOrWhiteSpace(contract.Detail));
        Assert.False(string.IsNullOrWhiteSpace(contract.TraceId));
    }

    private static string CreateJwtToken(
        string signingKey,
        DateTimeOffset issuedAtUtc,
        Guid userId,
        DateTimeOffset? expiresAtUtc = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtIssuer,
            Audience = JwtAudience,
            NotBefore = issuedAtUtc.UtcDateTime,
            Expires = (expiresAtUtc ?? issuedAtUtc.AddMinutes(60)).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "auditor@example.com"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ]),
            SigningCredentials = credentials,
            IssuedAt = issuedAtUtc.UtcDateTime
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }

        private sealed class ControlledHost : IAsyncDisposable
        {
        private sealed class PayloadLimiterState
        {
            public bool ProducedStatusOnly;
        }

        private ControlledHost(
            WebApplication application,
            HttpClient client,
            PayloadLimiterState payloadLimiterState)
        {
            Application = application;
            Client = client;
            _payloadLimiterState = payloadLimiterState;
        }

        public WebApplication Application { get; }
        public HttpClient Client { get; }
        public bool PayloadLimiterProducedStatusOnly => _payloadLimiterState.ProducedStatusOnly;

        private readonly PayloadLimiterState _payloadLimiterState;

        public static async Task<ControlledHost> StartAsync(
            bool withJwt,
            bool withControllers,
            bool enforcePayloadLimit = false)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName =
                        typeof(ProjectsController).Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Critical);

            var authenticationOptions = CotizadorAuthenticationOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Authentication:Google:ClientId"] = "test.google.client",
                            ["Authentication:Jwt:Issuer"] = JwtIssuer,
                            ["Authentication:Jwt:Audience"] = JwtAudience,
                            ["Authentication:Jwt:SigningKey"] = JwtSigningKey,
                            ["Authentication:Jwt:AccessTokenMinutes"] = "120"
                        })
                    .Build());

            builder.Services.AddPreQuoteProblemDetailsContract();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

            if (withControllers)
            {
                builder.Services.AddControllers()
                    .AddApplicationPart(typeof(ProjectsController).Assembly);
            }

            if (withJwt)
            {
                builder.Services.AddAuthorization(options =>
                {
                    options.AddPolicy(
                        "forbidden-policy",
                        policy => policy.RequireAssertion(_ => false));
                });

                builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.RequireHttpsMetadata = false;
                        options.SaveToken = false;
                        options.IncludeErrorDetails = false;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = authenticationOptions.Jwt.Issuer,
                            ValidateAudience = true,
                            ValidAudience = authenticationOptions.Jwt.Audience,
                            ValidateLifetime = true,
                            RequireExpirationTime = true,
                            RequireSignedTokens = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(
                                    authenticationOptions.Jwt.SigningKey)),
                            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                            ClockSkew = TimeSpan.FromMinutes(1)
                        };

                        options.Events = new JwtBearerEvents
                        {
                            OnChallenge = async context =>
                            {
                                if (ApiProblemDetailsFactory.IsContractualRequest(
                                        context.HttpContext))
                                {
                                    context.HandleResponse();
                                    await ApiProblemDetailsFactory.WriteUnauthorizedAsync(
                                        context.HttpContext);
                                }
                            },
                            OnForbidden = async context =>
                            {
                                if (ApiProblemDetailsFactory.IsContractualRequest(
                                        context.HttpContext))
                                {
                                    await ApiProblemDetailsFactory.WriteForbiddenAsync(
                                        context.HttpContext);
                                }
                            }
                        };
                    });
            }

            builder.Services.AddAuthorization();

            if (withJwt)
            {
                // For the protected endpoint authentication, so tests
                // exercise the real JWT middleware path.
            }

            var application = builder.Build();

            application.UseExceptionHandler();
            application.UseRouting();
            application.UseContractualProblemDetails();
            var payloadLimiterState = new PayloadLimiterState();

            if (enforcePayloadLimit)
            {
                application.Use(async (context, next) =>
                {
                    if (context.Request.Path == "/api/v1/projects"
                        && context.Request.ContentLength.GetValueOrDefault() > 1024
                        && (HttpMethods.IsPost(context.Request.Method)
                            || HttpMethods.IsPut(context.Request.Method)))
                    {
                        payloadLimiterState.ProducedStatusOnly = true;
                        context.Response.StatusCode =
                            StatusCodes.Status413PayloadTooLarge;
                        return;
                    }

                    await next(context);
                });
            }

            if (withJwt)
            {
                application.UseAuthentication();
            }

            application.UseAuthorization();

            if (withControllers)
            {
                application.MapControllers();
            }
            else
            {
                var projectContractualErrors = new ContractualErrorsAttribute
                {
                    InvalidRequestErrorCode = ApiErrorCodes.InternalServerError,
                    MethodNotAllowedErrorCode = ApiErrorCodes.ApiMethodNotAllowed,
                    PayloadTooLargeErrorCode = ApiErrorCodes.ApiPayloadTooLarge
                };

                application.MapGet("/api/v1/projects", () => TypedResults.Ok(new
                {
                    Value = "projects"
                })).WithMetadata(projectContractualErrors);
                application.MapPost("/api/v1/projects", () => TypedResults.Ok()).WithMetadata(
                    projectContractualErrors);

                application.MapGet("/api/v1/be-err-audit/unexpected-failure", () =>
                    throwUnexpectedFailure())
                    .WithMetadata(new ContractualErrorsAttribute
                    {
                        InvalidRequestErrorCode = ApiErrorCodes.InternalServerError
                    });
                application.MapGet(
                    "/api/v1/projects/bad-request",
                    throwBadRequestException);

                application.MapGet("/api/v1/be-err-audit/protected", () => TypedResults.Ok(new
                {
                    Value = "protected"
                })).WithMetadata(new ContractualErrorsAttribute
                {
                    InvalidRequestErrorCode = ApiErrorCodes.AuthUnauthorized,
                    UnauthorizedErrorCode = ApiErrorCodes.AuthUnauthorized,
                    MethodNotAllowedErrorCode = ApiErrorCodes.ApiMethodNotAllowed
                }).RequireAuthorization();

                application.MapGet("/api/v1/be-err-audit/forbidden-policy", () =>
                    TypedResults.Ok(new
                    {
                        Value = "forbidden"
                    }))
                    .WithMetadata(new ContractualErrorsAttribute
                    {
                        InvalidRequestErrorCode = ApiErrorCodes.AuthUnauthorized,
                        UnauthorizedErrorCode = ApiErrorCodes.AuthUnauthorized,
                        ForbiddenErrorCode = ApiErrorCodes.AuthForbidden,
                        MethodNotAllowedErrorCode = ApiErrorCodes.ApiMethodNotAllowed
                    })
                    .RequireAuthorization("forbidden-policy");
            }

            await application.StartAsync();
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            Assert.NotNull(addresses);
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses)) };
            return new ControlledHost(
                application,
                client,
                payloadLimiterState);
        }

        private static IResult throwUnexpectedFailure()
        {
            throw new InvalidOperationException("Unexpected audit failure");
        }

        private static IResult throwBadRequestException()
        {
            throw new BadHttpRequestException(
                "Payload too large in test",
                StatusCodes.Status413PayloadTooLarge);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
        }
    }
}
