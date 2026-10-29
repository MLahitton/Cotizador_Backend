using System.Security.Claims;
using Api.Controllers;
using Api.ErrorHandling;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed partial class ProcessRequirementServiceTests
{
    [Fact]
    public async Task LegacyProcessHttp_JobConflict_UsesSpecific409WithoutProvider()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.Requirements.BeginLegacyWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IRequirementLegacyWrite>(new LegacyProcessingRejectedException(ProcessRequirementFailure.ProcessingManagedByJob)));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(RequirementsProcessingController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient<RequirementsProcessingController>(_ => new(context.Service, null!));
        builder.Services.AddAuthorization();
        builder.Services.AddPreQuoteProblemDetailsContract();
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (http, next) =>
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"));
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
            using var response = await client.PostAsync($"/api/v2/requirements/{context.Requirement.Id}/process", null, TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("PROCESSING_MANAGED_BY_JOB", body);
            Assert.DoesNotContain("REQUIREMENT_PROCESSING_ALREADY_ACTIVE", body);
            Assert.Empty(context.Ai2.ReceivedCalls());
            Assert.Empty(context.CancellationRegistry.ReceivedCalls());
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }
}
