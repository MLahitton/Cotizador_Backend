using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api.Controllers;
using Api.ErrorHandling;
using Contracts.Common;
using Contracts.PreQuotes;
using CotizadorBackend.Tests.Application.PreQuotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class RequirementProcessingCancellationHttpTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var byRequirement in new[] { false, true })
        foreach (var scenario in new[] { "owner", "other", "admin-other", "inactive", "missing-user", "finished-other", "managed" })
            yield return [byRequirement, scenario, scenario switch
            {
                "owner" => 200, "inactive" => 403, "missing-user" => 401, "managed" => 409, _ => 404
            }];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BothRoutes_ApplyOwnerOnlyPolicy_WithoutRealDatabase(bool byRequirement, string scenario, int status)
    {
        var context = CancelRequirementProcessingAttemptServiceTests.Create(scenario);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RequirementsProcessingController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(RequirementsProcessingController).Assembly)
            .AddControllersAsServices();
        // Only cancellation routes are exercised; the normal processing service is never invoked.
        builder.Services.AddTransient<RequirementsProcessingController>(_ => new(null!, context.Service));
        builder.Services.AddAuthorization();
        builder.Services.AddPreQuoteProblemDetailsContract();
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (http, next) =>
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, context.User.Id.ToString())], "Test"));
            await next(http);
        });
        app.UseAuthorization();
        app.UseContractualProblemDetails();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var path = byRequirement
                ? $"/api/v2/requirements/{context.Requirement.Id}/process/cancel"
                : $"/api/v2/processing-attempts/{context.Attempt.Id}/cancel";
            using var response = await client.PostAsync(path, null, TestContext.Current.CancellationToken);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            if (status == 200)
            {
                var body = await response.Content.ReadFromJsonAsync<ProcessRequirementResponse>(TestContext.Current.CancellationToken);
                Assert.Equal(context.Attempt.Id, body!.ProcessingAttemptId);
                Assert.Equal("Finished", body.ProcessingState);
                Assert.Equal("Cancelled", body.Outcome);
            }
            else
            {
                var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetailsResponse>(TestContext.Current.CancellationToken);
                Assert.NotNull(problem);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.Contains(status switch
                {
                    403 => PreQuoteErrorCodes.InactiveUser,
                    401 => PreQuoteErrorCodes.Unauthorized,
                    409 => "PROCESSING_MANAGED_BY_JOB",
                    _ => RequirementErrorCodes.ProcessingAttemptNotFound
                }, body);
                Assert.DoesNotContain(context.Attempt.CorrelationId.ToString(), body);
                Assert.DoesNotContain(context.Project.CreatedByUserId.ToString(), body);
                CancelRequirementProcessingAttemptServiceTests.AssertNoMutation(context);
            }
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}
