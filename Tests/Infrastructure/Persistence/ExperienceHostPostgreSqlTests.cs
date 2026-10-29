using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Api.Controllers;
using Application;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessRequirement;
using Application.PublicExperience;
using Contracts.PublicExperience;
using CotizadorBackend.Tests.Api.Integration;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Infrastructure;
using Infrastructure.Authentication;
using Infrastructure.DocumentProcessing;
using Infrastructure.Operations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class HostProbe
    {
        internal readonly TaskCompletionSource Discovery = Signal();
        internal readonly TaskCompletionSource Release = Signal();
        internal readonly Channel<Guid> Finished = Channel.CreateUnbounded<Guid>();
        internal int Queries, Invocations;
    }
    private sealed class GatedCandidates(ExperienceCandidateReader reader, HostProbe probe) : IExperienceCandidateReader
    {
        public async Task<IReadOnlyList<ExperienceCandidate>> ReadAsync(ExperienceCandidate? after, int limit, CancellationToken ct)
        {
            Interlocked.Increment(ref probe.Queries); probe.Discovery.TrySetResult();
            await probe.Release.Task.WaitAsync(ct);
            return await reader.ReadAsync(after, limit, ct);
        }
    }
    private sealed class TrackedExecution(ExperienceInitialExecution inner, HostProbe probe) : IExperienceInitialExecution
    {
        public async Task ExecuteAsync(Guid jobId, CancellationToken ct)
        {
            Interlocked.Increment(ref probe.Invocations);
            try { await inner.ExecuteAsync(jobId, ct); }
            finally { probe.Finished.Writer.TryWrite(jobId); }
        }
    }
    private IConfiguration HostConfiguration(bool enabled = true)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Api", "appsettings.json"))) root = root.Parent;
        Assert.NotNull(root);
        return new ConfigurationBuilder().AddJsonFile(Path.Combine(root.FullName, "Api", "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "unused-experience-host"),
                ["CotizadorAi:BaseUrl"] = "http://127.0.0.1:1/",
                ["CotizadorAi2:BaseUrl"] = "http://127.0.0.1:1/",
                ["ExperienceExecutionHost:Enabled"] = enabled.ToString(),
                ["ExperienceExecutionHost:BatchSize"] = "1",
                ["ExperienceExecutionHost:PollInterval"] = "00:00:05",
                ["PublicExperience:EmitAutomaticExecution"] = "true",
                ["PublicExperience:OwnerEmail"] = "owner@example.test"
            }).Build();
    }
    private void HostServices(IServiceCollection services, Files files, ConsumingClient client,
        ManualSupervisionClock clock, HostProbe probe, bool enabled = true)
    {
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(HostConfiguration(enabled), new(new("test-client"), new("test", "test", new string('x', 32), 10)));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IFileStorage>(files);
        services.AddScoped<IVerifiedExecutionDocumentClient>(_ => client);
        services.AddScoped<ExperienceCandidateReader>();
        services.AddScoped<IExperienceCandidateReader>(sp => new GatedCandidates(sp.GetRequiredService<ExperienceCandidateReader>(), probe));
        services.AddScoped<ExperienceInitialExecution>();
        services.AddScoped<IExperienceInitialExecution>(sp => new TrackedExecution(sp.GetRequiredService<ExperienceInitialExecution>(), probe));
    }

    [Fact]
    public async Task ExperienceHost_HttpReturnsThenHostPublishes_DIRealGraph_OneExecution()
    {
        fixture.RequireAvailable(); await fixture.ResetAsync();
        await using var files = new Files(); var client = new ConsumingClient();
        var clock = new ManualSupervisionClock(); var probe = new HostProbe();
        await using (var db = fixture.CreateDbContext()) { db.Users.Add(ProcessPublicExperienceSubmissionTests.Owner()); await db.SaveChangesAsync(Token); }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(PublicExperienceSubmissionsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateScopes = true);
        HostServices(builder.Services, files, client, clock, probe);
        builder.Services.AddControllers().AddApplicationPart(typeof(PublicExperienceSubmissionsController).Assembly);
        await using var app = builder.Build(); app.MapControllers();
        await app.StartAsync(Token);
        try
        {
            await probe.Discovery.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new(address) };
            var uploads = new[] { PublicExperienceIntakeTests.Upload("abc"u8.ToArray()) };
            var payload = PublicExperienceIntakeTests.Payload(uploads);
            async Task<HttpResponseMessage> Post()
            {
                using var form = PublicExperienceSubmissionHttpTests.Multipart(payload, [PublicExperienceIntakeTests.Upload("abc"u8.ToArray())]);
                return await http.PostAsync("/api/public/experience-submissions", form, Token);
            }
            using var first = await Post(); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var receipt = await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
            Assert.NotNull(receipt); Assert.Equal(0, client.Calls); Assert.Equal(0, probe.Invocations);
            probe.Release.TrySetResult();
            var jobId = await SupervisionRead(probe.Finished);
            await using var read = fixture.CreateDbContext(); var job = await read.RequirementProcessingJobs.SingleAsync(Token);
            Assert.Equal(job.Id, jobId); Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
            Assert.Equal(RequirementProcessingJob.PublicationV2, job.PublicationContractVersion);
            Assert.Single(await read.RequirementProcessingAttempts.ToListAsync(Token));
            Assert.Single(await read.RequirementTechnicalProposals.ToListAsync(Token)); Assert.Equal(1, client.Calls);
            using var replay = await Post(); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(receipt with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
            Assert.Empty(await new ExperienceCandidateReader(H.Options()).ReadAsync(null, 16, Token));
            Assert.Equal(1, probe.Invocations); Assert.All(client.Readers, s => Assert.False(s.CanRead));
            Assert.All(client.Bytes, b => Assert.Equal("abc"u8.ToArray(), b));
        }
        finally { probe.Release.TrySetResult(); await app.StopAsync(Token); }
    }

    [Fact]
    public async Task ExperienceHost_Disabled_DIResolvesVerifiedTransport_NoDiscovery()
    {
        fixture.RequireAvailable(); await fixture.ResetAsync();
        await using var files = new Files(); var client = new ConsumingClient(); var probe = new HostProbe();
        var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
        HostServices(builder.Services, files, client, new(), probe, enabled: false);
        // Restore the production transport registration without sending any request.
        builder.Services.AddScoped<IVerifiedExecutionDocumentClient, SingleRequestAi2Client>();
        using var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.IsType<SingleRequestAi2Client>(scope.ServiceProvider.GetRequiredService<IVerifiedExecutionDocumentClient>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<SuperviseAssignedExecutionService>());
        }
        await host.StartAsync(Token); await host.StopAsync(Token);
        Assert.Equal(0, probe.Queries); Assert.Equal(0, probe.Invocations); Assert.Equal(0, client.Calls);
    }

    [Theory]
    [InlineData("revoked")] [InlineData("expired")] [InlineData("admitted")]
    [InlineData("running")] [InlineData("terminal")] [InlineData("manual")]
    public async Task ExperienceHost_Selection_ExcludesNonCandidates(string state)
    {
        await using var files = new Files(); var (s, permit) = await PermitSetup(files);
        if (state == "running") Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(
            new(s.Execution.JobId, s.Execution.RequirementId, s.Execution.ExecutionRequestId, permit.Admission.ClaimOperationId,
                permit.Admission.InstanceId) { Permit = permit }, s.Actor, Token)).State);
        await using (var db = fixture.CreateDbContext())
        {
            if (state is not ("admitted" or "running"))
                await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET dispatch_admitted_at_utc=NULL, dispatch_instance_id=NULL, dispatch_claim_operation_id=NULL", Token);
            if (state == "revoked") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET authorization_revoked_at_utc=authorized_at_utc", Token);
            if (state == "expired") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET created_at_utc=created_at_utc-interval '2 days', authorized_at_utc=authorized_at_utc-interval '2 days', authorization_expires_at_utc=authorization_expires_at_utc-interval '2 days'", Token);
            if (state == "terminal") { var job = await db.RequirementProcessingJobs.SingleAsync(Token); job.RequestCancellation(await H.Now()); await db.SaveChangesAsync(Token); }
            if (state == "manual") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET automatic_authorization_policy_version=NULL, authorized_actor_user_id=NULL, authorized_at_utc=NULL, authorization_expires_at_utc=NULL, required_publication_version='sng-job-publication-v1'", Token);
        }
        var before = await CombinedSnapshot();
        Assert.Empty(await new ExperienceCandidateReader(H.Options()).ReadAsync(null, 1, Token));
        Assert.Equal(before, await CombinedSnapshot());
    }
}
