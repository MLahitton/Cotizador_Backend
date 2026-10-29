using System.Net;
using Api.Controllers;
using Application.PublicExperience;
using CotizadorBackend.Tests.Api.Integration;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Infrastructure.Operations;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Fact]
    public async Task ExperienceHost_RealKeyset_IneligibleOlderJobDoesNotStarveValidJob()
    {
        await using var files = new Files(); var seed = await HostQueued(files); Guid invalidId;
        await using (var db = fixture.CreateDbContext())
        {
            var good = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
            var quote = await db.PreQuotes.AsNoTracking().SingleAsync(Token);
            var project = await db.Projects.AsNoTracking().SingleAsync(Token);
            var old = good.CreatedAtUtc.AddMinutes(-1);
            var badQuote = PreQuote.Create(project.Id, seed.Actor, "PC-2026-0999", null, old);
            var badRequirement = Requirement.Create(badQuote.Id, seed.Actor, RequirementCommercialLine.Essential, old);
            var submission = ProcessPublicExperienceSubmissionTests.Submission(withFile: false);
            submission.BeginProcessing(old); submission.CompleteProcessing(project.ClientId, project.Id, old);
            submission.CompleteRequirementPreparation(badQuote.Id, badRequirement.Id, old);
            var bad = RequirementProcessingJob.CreateForAutomaticExperience(Guid.NewGuid(), badRequirement.Id, submission.Id,
                Guid.NewGuid(), Guid.NewGuid(), seed.Actor, 1, old, old.AddHours(24));
            invalidId = bad.Id; db.AddRange(badQuote, badRequirement, submission, bad); await db.SaveChangesAsync(Token);
        }
        var reader = new ExperienceCandidateReader(H.Options()); var page = Assert.Single(await reader.ReadAsync(null, 1, Token));
        Assert.Equal(invalidId, page.Id); Assert.Equal(seed.Execution.JobId, Assert.Single(await reader.ReadAsync(page, 1, Token)).Id);
        var probe = new HostProbe(); probe.Release.TrySetResult(); var clock = new ManualSupervisionClock(); var client = new ConsumingClient();
        using var host = DatabaseHost(files, client, clock, probe); await host.StartAsync(Token);
        try
        {
            Assert.Equal(invalidId, await SupervisionRead(probe.Finished)); Assert.Equal(0, client.Calls);
            Assert.Equal(TimeSpan.FromSeconds(5), await SupervisionRead(clock.Scheduled));
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(seed.Execution.JobId, await SupervisionRead(probe.Finished)); Assert.Equal(1, client.Calls);
            await using var read = fixture.CreateDbContext();
            var bad = await read.RequirementProcessingJobs.SingleAsync(x => x.Id == invalidId, Token);
            Assert.Null(bad.DispatchAdmittedAtUtc); Assert.Equal(RequirementProcessingJobState.Queued, bad.State);
            Assert.Equal(RequirementProcessingJobState.Succeeded, (await read.RequirementProcessingJobs.SingleAsync(x => x.Id == seed.Execution.JobId, Token)).State);
        }
        finally { await host.StopAsync(Token); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task ExperienceHost_EmissionAndHostingAreIndependent_HttpCompatibility(bool emission, bool enabled)
    {
        fixture.RequireAvailable(); await fixture.ResetAsync();
        await using var files = new Files(); var client = new ConsumingClient(); var probe = new HostProbe();
        var clock = new ManualSupervisionClock();
        await using (var db = fixture.CreateDbContext()) { db.Users.Add(ProcessPublicExperienceSubmissionTests.Owner()); await db.SaveChangesAsync(Token); }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(PublicExperienceSubmissionsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        HostServices(builder.Services, files, client, clock, probe, enabled);
        builder.Services.Configure<PublicExperienceOptions>(o => o.EmitAutomaticExecution = emission);
        builder.Services.AddControllers().AddApplicationPart(typeof(PublicExperienceSubmissionsController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync(Token);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new(address) };
            var uploads = new[] { PublicExperienceIntakeTests.Upload("abc"u8.ToArray()) };
            var payload = PublicExperienceIntakeTests.Payload(uploads);
            async Task<HttpStatusCode> Post()
            {
                using var form = PublicExperienceSubmissionHttpTests.Multipart(payload, [PublicExperienceIntakeTests.Upload("abc"u8.ToArray())]);
                using var response = await http.PostAsync("/api/public/experience-submissions", form, Token);
                return response.StatusCode;
            }
            Assert.Equal(HttpStatusCode.Created, await Post()); Assert.Equal(HttpStatusCode.OK, await Post());
            probe.Release.TrySetResult();
            if (enabled) await SupervisionRead(clock.Scheduled);
            await using var read = fixture.CreateDbContext();
            Assert.Equal(emission ? 1 : 0, await read.RequirementProcessingJobs.CountAsync(Token));
            Assert.Equal(RequirementStatus.Pending, (await read.Requirements.SingleAsync(Token)).Status);
            Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token)); Assert.Empty(await read.JobLeaseOperations.ToListAsync(Token));
            Assert.Equal(0, client.Calls); Assert.Equal(0, probe.Invocations);
            if (!enabled) Assert.Equal(0, probe.Queries);
        }
        finally { probe.Release.TrySetResult(); await app.StopAsync(Token); }
    }
}
