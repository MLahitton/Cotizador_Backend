using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Api.Controllers;
using Api.ErrorHandling;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    [Theory]
    [InlineData("documents", false, "REQUIREMENT_NOT_MUTABLE")]
    [InlineData("cancel", false, "REQUIREMENT_NOT_MUTABLE")]
    [InlineData("replacement", false, "REQUIREMENT_NOT_REPLACEABLE")]
    [InlineData("replacement", true, "REQUIREMENT_NOT_FOUND")]
    public async Task FrozenHttp_ReturnsExistingConflictOrNonrevealingNotFound(string route, bool foreignAdmin, string code)
    {
        var seed = await SeedState("queued");
        var actor = seed.Actor;
        if (foreignAdmin)
        {
            await using var db = fixture.CreateDbContext();
            var now = DateTimeOffset.UtcNow;
            var user = User.CreateFromGoogle("admin@doc.test", "Admin", null, null, now);
            user.ChangeRole(UserRole.Admin, now);
            actor = user.Id;
            db.Add(user);
            await db.SaveChangesAsync(Token);
        }
        using var storage = await Files(seed);
        var original = await Protocol.P.Snapshot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(PreQuoteRequirementsController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient<PreQuoteRequirementsController>(_ => new(null!));
        builder.Services.AddScoped<ApplicationDbContext>(_ => fixture.CreateDbContext());
        builder.Services.AddScoped<ManageRequirementDocumentsService>(s => Service(s.GetRequiredService<ApplicationDbContext>(), actor, storage));
        builder.Services.AddAuthorization();
        builder.Services.AddPreQuoteProblemDetailsContract();
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (http, next) =>
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "Test"));
            await next(http);
        });
        app.UseAuthorization();
        app.UseContractualProblemDetails();
        app.MapControllers();
        await app.StartAsync(Token);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent([37, 80, 68, 70]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipart.Add(file, "files", "file.pdf");
            using var response = await client.PostAsync($"/api/v2/requirements/{seed.Execution.RequirementId}/{route}",
                route == "cancel" ? null : multipart, Token);
            Assert.Equal(foreignAdmin ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Token);
            Assert.Contains(code, body);
            Assert.DoesNotContain("REQUIREMENT_PROCESSING_ALREADY_ACTIVE", body);
            Assert.DoesNotContain(seed.Execution.JobId.ToString(), body);
            Assert.Equal(original, await Protocol.P.Snapshot());
            Assert.Equal(0, storage.Writes);
            Assert.Equal(0, storage.Deletes);
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }
}
