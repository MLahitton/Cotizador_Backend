using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using Application.PreQuotes.ProcessRequirement;
using Application.PreQuotes.RequirementExperience;
using Application.Common.Abstractions.Catalogs;
using Application.Projects.GetProjectWorkspace;
using Contracts.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed partial class PublicExperienceHttpPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("DIRECT_CONTACT")]
    [InlineData("ANDRES_CANDELA")]
    public async Task Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict(string advisor)
    {
        await using var host = await StartAsync();
        var payload = Payload(advisor);
        using var first = await host.PostAsync(payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
        Assert.Equal("RECEIVED", created!.Status);
        Assert.False(created.IsReplay);
        await AssertChainAsync(created, host.Storage);
        Assert.Equal(0, host.Ai.Calls);
        using var retry = await host.PostAsync(payload);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var replay = await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
        Assert.Equal(created with { IsReplay = true }, replay);
        using var conflict = await host.PostAsync(payload.Replace("Example", "Changed"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertChainAsync(created, host.Storage);
        var body = await first.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("ESSENTIAL", body);
        Assert.DoesNotContain("technicalProposalId", body);
        Assert.DoesNotContain("processingAttemptId", body);
        Assert.DoesNotContain("processingOutcome", body);
        Assert.Equal(0, host.Ai.Calls);
        Assert.Equal(2, host.Storage.Writes);
    }

    [Fact]
    public async Task ConcurrentHttpPosts_CreateExactlyOnePair()
    {
        await using var host = await StartAsync();
        var responses = await Task.WhenAll(host.PostAsync(Payload()), host.PostAsync(Payload()));
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, response => Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
            using var retry = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var replay = (await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
            Assert.True(replay.IsReplay);
            Assert.Equal("RECEIVED", replay.Status);
            Assert.Equal(0, host.Ai.Calls);
            await AssertChainAsync(replay, host.Storage, competing: true);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task PrivateUploadOnPublicProject_PersistsEvidenceWithoutChangingPublicSource()
    {
        await using var host = await StartAsync();
        using var first = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = (await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
        string answers;
        string fingerprint;
        Guid originalId;
        await using (var read = fixture.CreateDbContext())
        {
            var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
            answers = submission.AnswersJson;
            fingerprint = submission.PayloadFingerprint;
            originalId = (await read.RequirementFiles.SingleAsync(Token)).Id;
        }
        var root = Directory.CreateTempSubdirectory("sng-private-public-").FullName;
        try
        {
            var storage = new global::Infrastructure.Storage.LocalFileStorage(
                new global::Infrastructure.Storage.FileStorageOptions(root), Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>());
            await using (var db = fixture.CreateDbContext())
            {
                var actorId = (await db.Users.SingleAsync(Token)).Id;
                var actor = Substitute.For<ICurrentUser>();
                actor.IsAuthenticated.Returns(true);
                actor.UserId.Returns(actorId);
                var service = new global::Application.PreQuotes.ManageRequirementDocuments.ManageRequirementDocumentsService(
                    actor, new IdentityRepository(db), new RequirementRepository(db), new PreQuoteRepository(db),
                    new ProjectRepository(db), new ClientRepository(db), storage, TimeProvider.System,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<global::Application.PreQuotes.ManageRequirementDocuments.ManageRequirementDocumentsService>.Instance);
                using var content = new MemoryStream("abc"u8.ToArray());
                Assert.True((await service.AddDocumentAsync(new(receipt.RequirementId!.Value,
                    new("private.pdf", "application/pdf", 3, content)), Token)).IsSuccess);
            }
            using var replay = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(receipt with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
            await using var verify = fixture.CreateDbContext();
            var source = await verify.Set<PublicExperienceSubmission>().SingleAsync(Token);
            Assert.Equal(answers, source.AnswersJson);
            Assert.Equal(fingerprint, source.PayloadFingerprint);
            var original = await verify.RequirementFiles.SingleAsync(f => f.Id == originalId, Token);
            Assert.NotNull(original.StoredSha256);
            Assert.Equal(original.SizeBytes, original.StoredLengthBytes);
            Assert.NotNull(original.StoredEvidenceAtUtc);
            Assert.Equal((short)1, original.StoredEvidenceVersion);
            var added = await verify.RequirementFiles.SingleAsync(f => f.Id != originalId, Token);
            Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", added.StoredSha256);
            Assert.Equal(3, added.StoredLengthBytes);
            Assert.Equal((short)1, added.StoredEvidenceVersion);
            Assert.NotNull(added.StoredEvidenceAtUtc);
            await using var bytes = await storage.OpenReadAsync(added.StorageKey, Token);
            using var buffer = new MemoryStream();
            await bytes.CopyToAsync(buffer, Token);
            Assert.Equal("abc"u8.ToArray(), buffer.ToArray());
            Assert.Equal(RequirementStatus.Pending, (await verify.Requirements.SingleAsync(Token)).Status);
            Assert.False(await verify.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await verify.RequirementProcessingAttempts.AnyAsync(Token));
            Assert.Equal(0, host.Ai.Calls);
            Assert.Equal(2, host.Storage.Writes);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessingValidationFailure_PreservesIntakeAndFiles_ExplicitRetry(bool invalidLocation)
    {
        await using var host = await StartAsync(invalidLocation ? "owner@example.test" : null);
        var payload = Payload();
        if (invalidLocation)
        {
            var json = JsonNode.Parse(payload)!;
            json["project"]!["subdivision"] = new string('x', 250);
            payload = json.ToJsonString();
        }
        using var response = await host.PostAsync(payload);
        Assert.Equal(invalidLocation ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("public-test-id", problem["submissionId"]!.GetValue<string>());
        Assert.Equal(invalidLocation ? PublicExperienceProcessingErrors.LocationInvalid
            : PublicExperienceProcessingErrors.OwnerNotAvailable, problem["processingErrorCode"]!.GetValue<string>());
        await using (var read = fixture.CreateDbContext())
        {
            var submission = await read.Set<PublicExperienceSubmission>().Include(x => x.Files).SingleAsync(Token);
            Assert.Equal("FAILED", submission.Status);
            Assert.Single(submission.Files);
            Assert.False(await read.Clients.AnyAsync(Token));
            Assert.False(await read.Projects.AnyAsync(Token));
        }
        Assert.Single(host.Storage.Files);
        if (!invalidLocation)
        {
            host.Options.OwnerEmail = "owner@example.test";
            using var retry = await host.PostAsync(payload);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var result = await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
            Assert.True(result!.IsReplay);
            await AssertChainAsync(result, host.Storage);
            Assert.Equal(2, host.Storage.Writes);
        }
    }

    [Fact]
    public async Task PersistedProcessing_ReturnsConflictWithoutRestart()
    {
        await using var host = await StartAsync();
        using (var scope = host.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PublicExperienceIntakeService>()
                .ExecuteAsync(Payload(), [PublicExperienceIntakeTests.Upload()], Token);
        await using (var context = fixture.CreateDbContext())
        {
            var submission = await context.Set<PublicExperienceSubmission>().SingleAsync(Token);
            submission.BeginProcessing(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(Token);
        }
        using var response = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var read = fixture.CreateDbContext();
        Assert.Equal("PROCESSING", (await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).Status);
        Assert.False(await read.Clients.AnyAsync(Token));
        Assert.False(await read.Projects.AnyAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseFailure_ReturnsProblemWithoutStack_AndRetainsIntake(bool codeConflict)
    {
        await using var host = await StartAsync();
        await using var context = fixture.CreateDbContext();
        // Database faults exercise the actual transaction, rollback and HTTP mapping.
        var failure = codeConflict
            ? "RAISE unique_violation USING CONSTRAINT = 'ux_projects_code';"
            : "RAISE EXCEPTION 'Injected private persistence failure';";
        await context.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION core.fail_public_project() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN "
            + failure + " RETURN NEW; END $body$; "
            + "CREATE TRIGGER fail_public_project BEFORE INSERT ON core.projects "
            + "FOR EACH ROW EXECUTE FUNCTION core.fail_public_project();", Token);
        try
        {
            using var response = await host.PostAsync(Payload());
            Assert.Equal(codeConflict ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Token);
            var problem = JsonNode.Parse(body)!;
            Assert.Equal("public-test-id", problem["submissionId"]!.GetValue<string>());
            Assert.Equal(codeConflict ? PublicExperienceProcessingErrors.ProjectCodeConflict
                : PublicExperienceProcessingErrors.ProcessingFailed, problem["processingErrorCode"]!.GetValue<string>());
            Assert.DoesNotContain("Injected", body);
            Assert.DoesNotContain("StackTrace", body);
            Assert.Equal("FAILED", (await context.Set<PublicExperienceSubmission>().SingleAsync(Token)).Status);
            Assert.False(await context.Clients.AnyAsync(Token));
            Assert.False(await context.Projects.AnyAsync(Token));
            Assert.Single(host.Storage.Files);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER fail_public_project ON core.projects; DROP FUNCTION core.fail_public_project();", Token);
        }
    }

    [Fact]
    public async Task NoDocuments_Returns422_AndReplayNeverCreatesBusinessEntities()
    {
        await using var host = await StartAsync();
        var payload = PublicExperienceIntakeTests.Payload();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await host.PostAsync(payload, withFile: false);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
            Assert.Equal(PublicExperienceRequirementPreparationErrors.DocumentsRequired,
                problem["processingErrorCode"]!.GetValue<string>());
            await using var context = fixture.CreateDbContext();
            var submission = await context.Set<PublicExperienceSubmission>().SingleAsync(Token);
            Assert.Equal("FAILED", submission.Status);
            Assert.Equal(PublicExperienceRequirementPreparationErrors.DocumentsRequired, submission.ProcessingErrorCode);
            Assert.Null(submission.ClientId);
            Assert.Null(submission.ProjectId);
            Assert.Null(submission.PreQuoteId);
            Assert.Null(submission.RequirementId);
            Assert.False(await context.Clients.AnyAsync(Token));
            Assert.False(await context.Projects.AnyAsync(Token));
            Assert.False(await context.PreQuotes.AnyAsync(Token));
            Assert.False(await context.Requirements.AnyAsync(Token));
            Assert.False(await context.RequirementFiles.AnyAsync(Token));
            Assert.False(await context.RequirementProcessingAttempts.AnyAsync(Token));
            Assert.False(await context.RequirementTechnicalProposals.AnyAsync(Token));
            Assert.False(await context.Set<PublicExperienceSubmissionFile>().AnyAsync(Token));
        }
        Assert.Empty(host.Storage.Files);
        Assert.Equal(0, host.Ai.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialReplay_ContinuesPreparation_UsingStoredSnapshot(bool corruptOriginal)
    {
        await using var host = await StartAsync();
        PublicExperienceProcessingResult? processed;
        using (var scope = host.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PublicExperienceIntakeService>()
                .ExecuteAsync(Payload(), [PublicExperienceIntakeTests.Upload()], Token);
            processed = await scope.ServiceProvider.GetRequiredService<ProcessPublicExperienceSubmissionService>()
                .ExecuteAsync("public-test-id", Token);
        }
        var originalKey = Assert.Single(host.Storage.Files).Key;
        if (corruptOriginal) host.Storage.Files[originalKey] = [9, 9, 9, 9];
        using var response = await host.PostAsync(Payload());
        if (corruptOriginal)
        {
            // The multipart retry is intact. A failure proves preparation reads stored bytes instead.
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await AssertPreparationFailureAsync(response, PublicExperienceRequirementPreparationErrors.FileIntegrity);
            Assert.Single(host.Storage.Files);
            Assert.Equal(new byte[] { 9, 9, 9, 9 }, host.Storage.Files[originalKey]);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
            Assert.True(result!.IsReplay);
            Assert.Equal(processed!.ClientId, result.ClientId);
            Assert.Equal(processed.ProjectId, result.ProjectId);
            await AssertChainAsync(result, host.Storage);
            Assert.Equal(2, host.Storage.Writes);
        }
    }

    [Fact]
    public async Task PreparationPersistenceFailure_Returns500_RetainsFiles_ThenHttpRetryCompletes()
    {
        await using var host = await StartAsync();
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION core.fail_http_preparation() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF NEW.requirement_id IS NOT NULL THEN RAISE EXCEPTION 'Private database failure'; END IF;
              RETURN NEW;
            END $body$;
            CREATE TRIGGER fail_http_preparation BEFORE UPDATE ON core.public_experience_submissions
            FOR EACH ROW EXECUTE FUNCTION core.fail_http_preparation();
            """, Token);
        Guid? clientId;
        Guid? projectId;
        try
        {
            using var response = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await AssertPreparationFailureAsync(response, PublicExperienceRequirementPreparationErrors.Failed);
            Assert.DoesNotContain("Private database", await response.Content.ReadAsStringAsync(Token));
            Assert.Equal(2, host.Storage.Files.Count);
            Assert.All(host.Storage.Files.Values, bytes => Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes));
            Assert.Equal(0, host.Storage.Deletes);
            var saved = await context.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token);
            clientId = saved.ClientId;
            projectId = saved.ProjectId;
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER fail_http_preparation ON core.public_experience_submissions;
                DROP FUNCTION core.fail_http_preparation();
                """, Token);
        }
        using var retry = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var result = await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
        Assert.True(result!.IsReplay);
        Assert.Equal(clientId, result.ClientId);
        Assert.Equal(projectId, result.ProjectId);
        await AssertChainAsync(result, host.Storage, expectedFileCount: 3);
    }

    [Fact]
    public async Task ManualProcessing_AfterPublicReceipt_CreatesProposalAndHistoricalReplayDoesNotProcess()
    {
        await using var host = await StartAsync();
        host.Ai.Scenario = "review";
        using var first = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
        Assert.Equal("RECEIVED", created.Status);
        Assert.Equal(0, host.Ai.Calls);
        await AssertChainAsync(created, host.Storage);
        var manual = await ProcessManuallyAsync(host, created.RequirementId!.Value);
        Assert.True(manual.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, manual.Attempt!.Outcome);
        await AssertChainAsync(created, host.Storage, processed: true);
        using var retry = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(created with { IsReplay = true },
            await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
        Assert.Equal(1, host.Ai.Calls);
        await AssertChainAsync(created, host.Storage, processed: true);
    }

    [Theory]
    [InlineData("unavailable", ProcessRequirementFailure.AiServiceUnavailable, "AI2_SERVICE_UNAVAILABLE")]
    [InlineData("timeout", ProcessRequirementFailure.AiTimeout, "AI2_TIMEOUT")]
    [InlineData("invalid", ProcessRequirementFailure.AiInvalidResponse, "AI_INVALID_RESPONSE")]
    [InlineData("rejected", ProcessRequirementFailure.AiRemoteRejected, "AI2_REQUEST_REJECTED")]
    [InlineData("service-error", ProcessRequirementFailure.AiServiceError, "AI2_SERVICE_ERROR")]
    [InlineData("storage", ProcessRequirementFailure.StorageError, "REQUIREMENT_STORAGE_ERROR")]
    [InlineData("persistence", ProcessRequirementFailure.PersistenceError, "REQUIREMENT_PERSISTENCE_ERROR")]
    public async Task ManualProcessingFailure_PersistsAndPublicReplayDoesNotRetry(string scenario, ProcessRequirementFailure failure, string error)
    {
        await using var host = await StartAsync();
        host.Ai.Scenario = scenario;
        host.Storage.FailRequirementReads = scenario == "storage";
        await using var db = fixture.CreateDbContext();
        if (scenario == "persistence")
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION core.fail_public_proposal() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Private proposal persistence failure'; END $body$;
                CREATE TRIGGER fail_public_proposal BEFORE INSERT ON core.requirement_technical_proposals
                FOR EACH ROW EXECUTE FUNCTION core.fail_public_proposal();
                """, Token);
        try
        {
            using var first = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var initial = (await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
            Assert.Equal(0, host.Ai.Calls);
            var manual = await ProcessManuallyAsync(host, initial.RequirementId!.Value);
            Assert.False(manual.IsSuccess);
            Assert.Equal(failure, manual.Failure);
            using var retry = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var replay = await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
            Assert.Equal(initial with { IsReplay = true }, replay);
            Assert.Equal(scenario == "storage" ? 0 : 1, host.Ai.Calls);
            var requirement = await db.Requirements.AsNoTracking().SingleAsync(Token);
            Assert.Equal(RequirementStatus.Failed, requirement.Status);
            var attempt = await db.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
            Assert.Equal(error, attempt.ErrorCode);
            Assert.False(await db.RequirementExtractionResults.AnyAsync(Token));
            Assert.False(await db.RequirementTechnicalProposals.AnyAsync(Token));
        }
        finally
        {
            if (scenario == "persistence")
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER fail_public_proposal ON core.requirement_technical_proposals;
                    DROP FUNCTION core.fail_public_proposal();
                    """, Token);
        }
    }

    [Fact]
    public async Task PendingPartialReplay_RemainsPending_ThenManualProcessingLeavesExperienceUnanswered()
    {
        await using var host = await StartAsync();
        await PrepareOnlyAsync(host);
        Assert.Equal(0, host.Ai.Calls);
        using var response = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
        Assert.True(result.IsReplay);
        Assert.Equal(0, host.Ai.Calls);
        await AssertChainAsync(result, host.Storage);
        Assert.True((await ProcessManuallyAsync(host, result.RequirementId!.Value)).IsSuccess);
        Assert.Equal(1, host.Ai.Calls);
        await AssertChainAsync(result, host.Storage, processed: true);

        await using var db = fixture.CreateDbContext();
        var proposalId = (await db.RequirementTechnicalProposals.SingleAsync(Token)).Id;
        var owner = await db.Users.SingleAsync(Token);
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(owner.Id);
        var repository = new RequirementRepository(db);
        var identity = new IdentityRepository(db);
        var drafts = await new GetRequirementExperienceDraftsService(repository, identity, current)
            .ExecuteAsync(proposalId, Token);
        Assert.True(drafts.IsSuccess);
        Assert.NotEmpty(drafts.Value!.Items);
        Assert.All(drafts.Value.Items, item =>
        {
            Assert.Empty(item.Answers);
            Assert.True(item.Level1Resolution is null || item.Level1Resolution.AnsweredBenefits == 0);
        });
        await using var scope = host.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var level2 = new GetRequirementExperienceLevel2Service(repository, identity, current,
            services.GetRequiredService<IProductSystemCatalogRepository>(),
            services.GetRequiredService<IGlassTypeCatalogRepository>(),
            services.GetRequiredService<RequirementExperienceLevel2Resolver>());
        foreach (var item in drafts.Value.Items)
        {
            var resolved = await level2.ExecuteAsync(proposalId, item.TechnicalProposalItemId, Token);
            Assert.True(resolved.IsSuccess);
            Assert.Equal("PENDING", resolved.Value!.System.State);
            Assert.Equal("PENDING", resolved.Value.Glass.State);
            Assert.Null(resolved.Value.System.Effective);
            Assert.Null(resolved.Value.Glass.Effective);
        }
        var configuration = await new RequirementExperienceConfigurationService(repository, identity, current,
            services.GetRequiredService<IRequirementExperienceConfigurationResolver>(), TimeProvider.System)
            .GetStatusAsync(proposalId, Token);
        Assert.True(configuration.IsSuccess);
        Assert.False(await db.RequirementItemExperienceDrafts.AnyAsync(Token));
        var submission = await db.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.False(string.IsNullOrWhiteSpace(submission.AnswersJson));
    }

    [Theory]
    [InlineData("processed-missing")]
    [InlineData("invalid-links")]
    [InlineData("cancelled")]
    public async Task HistoricalReplay_DoesNotRunProcessingValidationOrCallAi(string scenario)
    {
        await using var host = await StartAsync();
        await PrepareOnlyAsync(host);
        await using var db = fixture.CreateDbContext();
        var requirement = await db.Requirements.SingleAsync(Token);
        if (scenario == "processed-missing")
        {
            requirement.StartProcessing(DateTimeOffset.UtcNow);
            requirement.MarkProcessed(DateTimeOffset.UtcNow);
        }
        else if (scenario == "cancelled") requirement.Cancel(DateTimeOffset.UtcNow);
        else
        {
            var original = await db.PreQuotes.SingleAsync(Token);
            var other = PreQuote.Create(original.ProjectId, original.CreatedByUserId, "PC-2026-9999", null, DateTimeOffset.UtcNow);
            db.PreQuotes.Add(other);
            await db.SaveChangesAsync(Token);
            await db.Requirements.Where(x => x.Id == requirement.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.PreQuoteId, other.Id), Token);
        }
        await db.SaveChangesAsync(Token);
        var before = System.Text.Json.JsonSerializer.Serialize(await db.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token));
        var writes = host.Storage.Writes;
        using var response = await host.PostAsync(Payload());
        if (scenario == "invalid-links")
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
            Assert.Equal(PublicExperienceRequirementPreparationErrors.InconsistentLinks, problem["processingErrorCode"]!.GetValue<string>());
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = (await response.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
            Assert.Equal("RECEIVED", receipt.Status);
            Assert.True(receipt.IsReplay);
        }
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await db.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token)));
        Assert.Equal(writes, host.Storage.Writes);
        Assert.Equal(requirement.Status, (await db.Requirements.AsNoTracking().SingleAsync(Token)).Status);
        Assert.Equal(0, host.Ai.Calls);
    }

    private static async Task<ProcessRequirementResult> ProcessManuallyAsync(Host host, Guid requirementId)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var owner = await db.Users.SingleAsync(Token);
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(owner.Id);
        return await new ProcessRequirementService(new ProcessRequirementCommandValidator(), current,
            services.GetRequiredService<ProcessRequirementCoreService>())
            .ExecuteAsync(new(requirementId), Token);
    }

    private static async Task PrepareOnlyAsync(Host host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<PublicExperienceIntakeService>()
            .ExecuteAsync(Payload(), [PublicExperienceIntakeTests.Upload()], Token);
        await services.GetRequiredService<ProcessPublicExperienceSubmissionService>().ExecuteAsync("public-test-id", Token);
        var prepared = await services.GetRequiredService<PreparePublicExperienceRequirementService>().ExecuteAsync("public-test-id", Token);
        Assert.True(prepared!.IsSuccess);
    }

    private async Task AssertPreparationFailureAsync(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        var problem = JsonNode.Parse(body)!;
        Assert.Equal("public-test-id", problem["submissionId"]!.GetValue<string>());
        Assert.Equal(expectedCode, problem["processingErrorCode"]!.GetValue<string>());
        Assert.DoesNotContain("StackTrace", body);
        await using var read = fixture.CreateDbContext();
        var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Equal("PROCESSED", submission.Status);
        Assert.Null(submission.ProcessingErrorCode);
        Assert.Equal(expectedCode, submission.RequirementPreparationErrorCode);
        Assert.Null(submission.RequirementPreparedAtUtc);
        Assert.Null(submission.PreQuoteId);
        Assert.Null(submission.RequirementId);
        Assert.Equal(1, await read.Clients.CountAsync(Token));
        Assert.Equal(1, await read.Projects.CountAsync(Token));
        Assert.False(await read.PreQuotes.AnyAsync(Token));
        Assert.False(await read.Requirements.AnyAsync(Token));
        Assert.False(await read.RequirementFiles.AnyAsync(Token));
        Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
    }

    private async Task AssertChainAsync(PublicExperienceSubmissionResponse response, IntakeStorage storage, bool processed = false, bool competing = false, int expectedFileCount = 2)
    {
        await using var read = fixture.CreateDbContext();
        var client = await read.Clients.SingleAsync(Token);
        var project = await read.Projects.SingleAsync(Token);
        var owner = await read.Users.SingleAsync(Token);
        Assert.Equal(ClientType.Person, client.ClientType);
        Assert.Equal(owner.Id, client.CreatedByUserId);
        Assert.Equal(owner.Id, project.CreatedByUserId);
        Assert.Equal(response.ClientId, client.Id);
        Assert.Equal(response.ProjectId, project.Id);
        Assert.Equal(client.Id, project.ClientId);
        var submission = await read.Set<PublicExperienceSubmission>().Include(x => x.Files).SingleAsync(Token);
        var preQuote = await read.PreQuotes.SingleAsync(Token);
        var requirement = await read.Requirements.SingleAsync(Token);
        Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
        Assert.Equal(response.PreQuoteId, preQuote.Id);
        Assert.Equal(response.RequirementId, requirement.Id);
        Assert.Equal(project.Id, preQuote.ProjectId);
        Assert.Equal(preQuote.Id, requirement.PreQuoteId);
        Assert.Equal(owner.Id, preQuote.CreatedByUserId);
        Assert.Equal(owner.Id, requirement.CreatedByUserId);
        Assert.Equal(RequirementCommercialLine.Essential, requirement.CommercialLine);
        Assert.Equal(processed ? RequirementStatus.Processed : RequirementStatus.Pending, requirement.Status);
        Assert.Equal(preQuote.Id, submission.PreQuoteId);
        Assert.Equal(requirement.Id, submission.RequirementId);
        Assert.Null(submission.RequirementPreparationErrorCode);
        Assert.NotNull(submission.RequirementPreparedAtUtc);
        var original = Assert.Single(submission.Files);
        Assert.Equal(original.SizeBytes, original.StoredLengthBytes);
        Assert.Equal((short)1, original.StoredEvidenceVersion);
        Assert.NotNull(original.StoredEvidenceAtUtc);
        var copy = await read.RequirementFiles.SingleAsync(Token);
        Assert.Equal(original.ContentHash, copy.StoredSha256);
        Assert.Equal(copy.SizeBytes, copy.StoredLengthBytes);
        Assert.NotNull(copy.StoredEvidenceAtUtc);
        Assert.Equal((short)1, copy.StoredEvidenceVersion);
        Assert.Equal(requirement.Id, copy.RequirementId);
        Assert.Equal(original.OriginalFileName, copy.OriginalFileName);
        Assert.Equal(original.ContentType, copy.ContentType);
        Assert.Equal(original.SizeBytes, copy.SizeBytes);
        Assert.StartsWith($"requirements/{requirement.Id:D}/", copy.StorageKey);
        Assert.NotEqual(original.StorageKey, copy.StorageKey);
        Assert.Equal(storage.Files[original.StorageKey], storage.Files[copy.StorageKey]);
        if (competing)
        {
            // Two intake requests and two preparation candidates may finish storage before recognition.
            Assert.InRange(storage.Files.Count, 2, 4);
            Assert.All(storage.Files.Values, bytes => Assert.Equal(storage.Files[original.StorageKey], bytes));
            Assert.Equal(0, storage.Deletes);
        }
        else Assert.Equal(expectedFileCount, storage.Files.Count);
        Guid? proposalId = null;
        Assert.False(await read.Set<RequirementExecutionEvidence>().AnyAsync(Token));
        Assert.False(await read.Set<RequirementExecutionDocument>().AnyAsync(Token));
        Assert.False(await read.JobLeaseOperations.AnyAsync(Token));
        Assert.False(await read.ExpiredExecutionCancellations.AnyAsync(Token));
        if (processed)
        {
            var attempt = await read.RequirementProcessingAttempts.SingleAsync(Token);
            Assert.Equal(owner.Id, attempt.RequestedByUserId);
            Assert.Single(await read.RequirementExtractionResults.ToListAsync(Token));
            Assert.Single(await read.RequirementExtractedItems.ToListAsync(Token));
            proposalId = (await read.RequirementTechnicalProposals.SingleAsync(Token)).Id;
        }
        else
        {
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
            Assert.False(await read.RequirementExtractionResults.AnyAsync(Token));
            Assert.False(await read.RequirementTechnicalProposals.AnyAsync(Token));
        }

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(owner.Id);
        var workspace = await new GetProjectWorkspaceService(new GetProjectWorkspaceQueryValidator(),
            currentUser, new IdentityRepository(read), new ProjectRepository(read), new ClientRepository(read),
            new PreQuoteRepository(read), new RequirementRepository(read), new PublicExperienceSubmissionRepository(read),
            new WorkspaceExtractionReader(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString).Options))
            .ExecuteAsync(new(project.Id), Token);
        Assert.True(workspace.IsSuccess);
        Assert.Equal(ProjectWorkspaceResolutionState.Resolved, workspace.Workspace!.Workflow.ResolutionState);
        Assert.Equal(preQuote.Id, workspace.Workspace.Workflow.PreQuoteId);
        Assert.Equal(requirement.Id, workspace.Workspace.Workflow.RequirementId);
        Assert.Equal(processed ? "PROCESSED" : "PENDING", workspace.Workspace.Workflow.RequirementStatus);
        Assert.Equal(proposalId, workspace.Workspace.Workflow.TechnicalProposalId);
        Assert.Equal(processed, workspace.Workspace.Workflow.HasTechnicalProposal);
        var migrations = await read.Database.GetAppliedMigrationsAsync(Token);
        Assert.Contains("20261016062338_AddPublicExperienceIntake", migrations);
        Assert.Contains("20261016065101_AddPublicExperienceProcessingLinks", migrations);
        Assert.Contains("20261016072800_AddPublicExperienceRequirementLinks", migrations);
    }

    private static string Payload(string advisor = "DIRECT_CONTACT") =>
        PublicExperienceIntakeTests.Payload([PublicExperienceIntakeTests.Upload()]).Replace("AD01", advisor);

    private async Task<Host> StartAsync(string? ownerEmail = "owner@example.test")
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
            await seed.SaveChangesAsync(Token);
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PublicExperienceSubmissionsController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        builder.Services.AddScoped<IPublicExperienceSubmissionRepository, PublicExperienceSubmissionRepository>();
        builder.Services.AddScoped<IPublicExperienceProcessingRepository, PublicExperienceProcessingRepository>();
        builder.Services.AddScoped<IPublicExperienceRequirementPreparationRepository, PublicExperienceRequirementPreparationRepository>();
        builder.Services.AddScoped<IPreQuoteRepository, PreQuoteRepository>();
        builder.Services.AddScoped<IRequirementRepository, RequirementRepository>();
        builder.Services.AddScoped<IRequirementProcessingLock, RequirementProcessingLock>();
        builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
        builder.Services.AddScoped<IClientRepository, ClientRepository>();
        builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddScoped<PublicExperienceIntakeService>();
        builder.Services.AddSingleton<IInitialContentCapture>(PublicExperienceIntakeTests.Capture());
        builder.Services.AddSingleton<IIdeaPhotoCapture, global::Infrastructure.Storage.IdeaPhotoCapture>();
        builder.Services.AddSingleton<IVerifiedContentCopy>(new global::Infrastructure.Storage.VerifiedContentCopy(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<global::Infrastructure.Storage.VerifiedContentCopy>.Instance));
        builder.Services.AddScoped<ProcessPublicExperienceSubmissionService>();
        builder.Services.AddScoped<PreparePublicExperienceRequirementService>();
        var ai = new PublicExperienceAiStub();
        PublicExperienceProcessingTestServices.Add(builder.Services, ai);
        builder.Services.AddSingleton(TimeProvider.System);
        var storage = new IntakeStorage();
        builder.Services.AddSingleton<IFileStorage>(storage);
        var settings = new PublicExperienceOptions { OwnerEmail = ownerEmail };
        builder.Services.AddSingleton<IOptions<PublicExperienceOptions>>(Options.Create(settings));
        builder.Services.AddControllers().AddApplicationPart(typeof(PublicExperienceSubmissionsController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(Token);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(app, new HttpClient { BaseAddress = new Uri(address) }, storage, settings, ai);
    }

    private sealed record Host(WebApplication App, HttpClient Client, IntakeStorage Storage,
        PublicExperienceOptions Options, PublicExperienceAiStub Ai) : IAsyncDisposable
    {
        public async Task<HttpResponseMessage> PostAsync(string payload, bool withFile = true)
        {
            using var multipart = PublicExperienceSubmissionHttpTests.Multipart(payload,
                withFile ? [PublicExperienceIntakeTests.Upload()] : []);
            return await Client.PostAsync("/api/public/experience-submissions", multipart, Token);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
