using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Api.Controllers;
using Application.PublicExperience;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.PreQuotes;
using Domain.PublicExperience;
using Domain.PreQuotes;
using Application.Common.Abstractions.Storage;
using Contracts.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class PublicExperienceSubmissionHttpTests
{
    [Fact]
    public async Task AnonymousMultipart_CreatedThenReplayThenConflict()
    {
        await using var host = await Host.StartAsync();
        var files = new[] { PublicExperienceIntakeTests.Upload() };
        var payload = PublicExperienceIntakeTests.Payload(files);
        using var first = await host.Client.PostAsync("/api/public/experience-submissions",
            Multipart(payload, files), TestContext.Current.CancellationToken);
        Assert.True(first.StatusCode == HttpStatusCode.Created,
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var response = await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal("RECEIVED", response!.Status);
        Assert.NotNull(response.ClientId);
        Assert.NotNull(response.ProjectId);
        Assert.NotNull(response.PreQuoteId);
        Assert.NotNull(response.RequirementId);
        Assert.False(response.IsReplay);
        using var retry = await host.Client.PostAsync("/api/public/experience-submissions",
            Multipart(payload, files), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var replay = await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(
            TestContext.Current.CancellationToken);
        Assert.True(replay!.IsReplay);
        Assert.Equal("RECEIVED", replay.Status);
        Assert.Equal(response.ClientId, replay.ClientId);
        Assert.Equal(response.ProjectId, replay.ProjectId);
        Assert.Equal(response.PreQuoteId, replay.PreQuoteId);
        Assert.Equal(response.RequirementId, replay.RequirementId);
        using var conflict = await host.Client.PostAsync("/api/public/experience-submissions",
            Multipart(payload.Replace("Example", "Changed"), files), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Single(host.Repository.Rows);
        Assert.Equal(files.Length * 2, host.Storage.Writes);
        Assert.Equal(0, host.Ai.Calls);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("source")]
    [InlineData("count")]
    [InlineData("field")]
    [InlineData("metadata")]
    [InlineData("internalId")]
    [InlineData("clientId")]
    [InlineData("ownerEmail")]
    [InlineData("userId")]
    [InlineData("projectCode")]
    [InlineData("storageKey")]
    public async Task InvalidMultipart_Returns400(string scenario)
    {
        await using var host = await Host.StartAsync();
        var file = PublicExperienceIntakeTests.Upload();
        var payload = PublicExperienceIntakeTests.Payload([file]);
        if (scenario == "schema") payload = payload.Replace("sng-public-experience-v1", "invalid");
        if (scenario == "source") payload = payload.Replace("SNG_WEB", "OTHER");
        if (scenario == "metadata") payload = payload.Replace("plan.pdf", "other.pdf");
        if (scenario == "internalId") payload = payload.Insert(1, "\"ProjectId\":\"forbidden\",");
        if (scenario is "clientId" or "ownerEmail" or "userId")
            payload = payload.Insert(1, $"\"{scenario}\":\"forbidden\",");
        if (scenario == "projectCode") payload = payload.Replace("\"project\":{", "\"project\":{\"code\":\"forbidden\",");
        if (scenario == "storageKey") payload = payload.Replace("\"fileName\":", "\"storageKey\":\"forbidden\",\"fileName\":");
        using var content = Multipart(payload, scenario == "count" ? [] : [file]);
        if (scenario == "field") content.Add(new StringContent("unknown"), "extra");
        using var response = await host.Client.PostAsync("/api/public/experience-submissions",
            content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Repository.Rows);
        Assert.Empty(host.Storage.Files);
    }

    internal static MultipartFormDataContent Multipart(string payload, IReadOnlyList<PublicExperienceUpload> files)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload");
        foreach (var file in files)
        {
            var part = new StreamContent(file.OpenRead());
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(part, "files", file.FileName);
        }
        return content;
    }

    private sealed class Host(WebApplication app, HttpClient client, IntakeRepository repository, IntakeStorage storage, PublicExperienceAiStub ai)
        : IAsyncDisposable
    {
        public HttpClient Client => client;
        public IntakeRepository Repository => repository;
        public IntakeStorage Storage => storage;
        public PublicExperienceAiStub Ai => ai;

        public static async Task<Host> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(PublicExperienceSubmissionsController).Assembly.GetName().Name,
                EnvironmentName = "Testing"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var repository = new IntakeRepository();
            var storage = new IntakeStorage();
            var ai = new PublicExperienceAiStub();
            PublicExperienceProcessingTestServices.Add(builder.Services, ai);
            builder.Services.AddSingleton<IPublicExperienceSubmissionRepository>(repository);
            builder.Services.AddSingleton<IFileStorage>(storage);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(PublicExperienceIntakeTests.Service(repository, storage));
            var identity = Substitute.For<IIdentityRepository>();
            var owner = ProcessPublicExperienceSubmissionTests.Owner();
            identity.FindUserByNormalizedEmailForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(owner);
            identity.FindUserByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
            builder.Services.AddSingleton(identity);
            var projects = Substitute.For<IProjectRepository>();
            global::Domain.Projects.Project? savedProject = null;
            projects.When(x => x.Add(Arg.Any<global::Domain.Projects.Project>()))
                .Do(call => savedProject = call.Arg<global::Domain.Projects.Project>());
            projects.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => savedProject);
            builder.Services.AddSingleton(projects);
            var clients = Substitute.For<IClientRepository>();
            global::Domain.Clients.Client? savedClient = null;
            clients.When(x => x.Add(Arg.Any<global::Domain.Clients.Client>()))
                .Do(call => savedClient = call.Arg<global::Domain.Clients.Client>());
            clients.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => savedClient);
            builder.Services.AddSingleton(clients);
            builder.Services.AddSingleton(new ProcessPublicExperienceSubmissionService(
                new HttpProcessingRepository(repository), identity, clients,
                projects, Options.Create(new PublicExperienceOptions
                    { OwnerEmail = "owner@example.test" }), TimeProvider.System));
            var preQuotes = Substitute.For<IPreQuoteRepository>();
            preQuotes.ReserveNextSerialAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns("PC-2026-0001");
            PreQuote? savedPreQuote = null;
            preQuotes.When(x => x.Add(Arg.Any<PreQuote>())).Do(call => savedPreQuote = call.Arg<PreQuote>());
            preQuotes.FindForUpdateByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => savedPreQuote);
            preQuotes.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => savedPreQuote is null ? null : new PreQuoteDetails(savedPreQuote.Id,
                    savedPreQuote.ProjectId, 1, savedPreQuote.CreatedAtUtc, savedPreQuote.UpdatedAtUtc));
            builder.Services.AddSingleton(preQuotes);
            var requirements = Substitute.For<IRequirementRepository>();
            Requirement? savedRequirement = null;
            RequirementTechnicalProposal? savedProposal = null;
            var files = new List<RequirementFile>();
            requirements.When(x => x.Add(Arg.Any<Requirement>())).Do(call => savedRequirement = call.Arg<Requirement>());
            requirements.When(x => x.AddFile(Arg.Any<RequirementFile>())).Do(call => files.Add(call.Arg<RequirementFile>()));
            requirements.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => savedRequirement);
            requirements.ListFilesByRequirementIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => (IReadOnlyList<RequirementFile>)files);
            requirements.When(x => x.AddProcessingAttempt(Arg.Any<RequirementProcessingAttempt>()))
                .Do(call => ((ICollection<RequirementProcessingAttempt>)savedRequirement!.ProcessingAttempts)
                    .Add(call.Arg<RequirementProcessingAttempt>()));
            requirements.When(x => x.AddTechnicalProposal(Arg.Any<RequirementTechnicalProposal>()))
                .Do(call => savedProposal = call.Arg<RequirementTechnicalProposal>());
            requirements.GetCurrentTechnicalProposalAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => savedProposal);
            builder.Services.AddSingleton(requirements);
            var processingLock = Substitute.For<IRequirementProcessingLock>();
            processingLock.TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => (IRequirementProcessingLease?)new MemoryLease(call.ArgAt<Guid>(0)));
            builder.Services.AddSingleton(processingLock);
            builder.Services.AddSingleton(new PreparePublicExperienceRequirementService(
                new HttpPreparationRepository(repository), projects, preQuotes, requirements,
                storage, TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance));
            builder.Services.AddControllers().AddApplicationPart(typeof(PublicExperienceSubmissionsController).Assembly);
            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync(TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, repository, storage, ai);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class HttpProcessingRepository(IntakeRepository intake) : IPublicExperienceProcessingRepository
    {
        public Task<PublicExperienceProcessingResult?> ProcessLockedAsync(string id,
            Func<PublicExperienceSubmission, CancellationToken, Task> process, CancellationToken cancellationToken) =>
            new ProcessingMemoryRepository(intake.Rows[id]).ProcessLockedAsync(id, process, cancellationToken);
    }

    private sealed class MemoryLease(Guid requirementId) : IRequirementProcessingLease
    {
        public Guid RequirementId => requirementId;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HttpPreparationRepository(IntakeRepository intake) : IPublicExperienceRequirementPreparationRepository
    {
        public async Task<PublicExperienceRequirementPreparationResult?> PrepareLockedAsync(string id,
            Func<PublicExperienceSubmission, CancellationToken, Task> prepare, Func<Task> compensate, CancellationToken token)
        {
            var submission = intake.Rows[id];
            var replay = submission.RequirementId is not null;
            if (!replay) await prepare(submission, token);
            return new(id, submission.PreQuoteId, submission.RequirementId, null, replay);
        }
    }
}
