using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Storage;
using Application.Projects.GetProjectWorkspace;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PublicExperienceRequirementPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("SNG_WEB", "{\"tipo\":\"casa\",\"tipo_otro\":\"ignored\",\"ventana_ejecucion\":\"4-6m\"}", "casa", null, "4-6m")]
    [InlineData("SNG_WEB", "{\"tipo\":\"otro\",\"tipo_otro\":\"Taller\"}", "otro", "Taller", null)]
    [InlineData("SNG_WEB", "{}", null, null, null)]
    [InlineData("OTHER", "{\"tipo\":\"casa\"}", null, null, null)]
    public async Task ProjectContext_ReadsOnlyLinkedPublicSubmission(
        string source, string answers, string? type, string? other, string? window)
    {
        var (submission, _) = await SeedAsync(1);
        await using var context = fixture.CreateDbContext();
        await context.Set<PublicExperienceSubmission>().Where(x => x.Id == submission.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Source, source)
                .SetProperty(x => x.AnswersJson, answers), Token);
        var repository = new PublicExperienceSubmissionRepository(context);
        var result = await repository.GetProjectContextByProjectIdAsync(submission.ProjectId!.Value, Token);
        if (source == "SNG_WEB") Assert.Equal(new PublicExperienceProjectContext(type, other, window), result);
        else Assert.Null(result);
        Assert.Null(await repository.GetProjectContextByProjectIdAsync(Guid.NewGuid(), Token));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Prepare_CopiesInOrder_OwnerBridgeSerialReplay_AndWorkspacePending(int count)
    {
        var (submission, storage) = await SeedAsync(count);
        await using var context = fixture.CreateDbContext();
        var service = Service(context, storage);
        var result = await service.ExecuteAsync(submission.SubmissionId, Token);
        Assert.True(result!.IsSuccess, result.ErrorCode);
        var replay = await service.ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal(result with { IsReplay = true }, replay);
        var preQuote = await context.PreQuotes.SingleAsync(Token);
        var requirement = await context.Requirements.SingleAsync(Token);
        var project = await context.Projects.SingleAsync(Token);
        Assert.Equal(project.Id, preQuote.ProjectId);
        Assert.Equal(project.CreatedByUserId, preQuote.CreatedByUserId);
        Assert.Equal(project.CreatedByUserId, requirement.CreatedByUserId);
        Assert.Equal(preQuote.Id, requirement.PreQuoteId);
        Assert.Equal(RequirementCommercialLine.Essential, requirement.CommercialLine);
        Assert.Equal(RequirementStatus.Pending, requirement.Status);
        Assert.Null(preQuote.Name);
        Assert.Equal(PreQuote.FormatSerial(DateTimeOffset.UtcNow.Year, 1), preQuote.Serial);
        var files = await new RequirementRepository(context).ListFilesByRequirementIdAsync(requirement.Id, Token);
        var originals = submission.Files.OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(count, files.Count);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(originals[i].OriginalFileName, files[i].OriginalFileName);
            Assert.Equal(originals[i].ContentType, files[i].ContentType);
            Assert.Equal(originals[i].SizeBytes, files[i].SizeBytes);
            Assert.StartsWith($"requirements/{requirement.Id:D}/", files[i].StorageKey);
            Assert.NotEqual(originals[i].StorageKey, files[i].StorageKey);
            Assert.Equal(storage.Files[originals[i].StorageKey], storage.Files[files[i].StorageKey]);
        }
        Assert.Equal(count * 2, storage.Files.Count);
        Assert.Equal(count * 2, storage.Writes);
        Assert.False(await context.RequirementProcessingAttempts.AnyAsync(Token));
        Assert.False(await context.RequirementTechnicalProposals.AnyAsync(Token));
        var persisted = await context.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Equal(result.PreQuoteId, persisted.PreQuoteId);
        Assert.Equal(result.RequirementId, persisted.RequirementId);
        Assert.NotNull(persisted.RequirementPreparedAtUtc);
        Assert.Null(persisted.RequirementPreparationErrorCode);
        Assert.Equal("PROCESSED", persisted.Status);
        Assert.Equal(submission.ClientId, persisted.ClientId);
        Assert.Equal(submission.ProjectId, persisted.ProjectId);

        // Only the existing read service uses a user context; the preparation service never does.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(project.CreatedByUserId);
        var workspace = await new GetProjectWorkspaceService(new GetProjectWorkspaceQueryValidator(),
            currentUser, new IdentityRepository(context), new ProjectRepository(context), new ClientRepository(context),
            new PreQuoteRepository(context), new RequirementRepository(context), new PublicExperienceSubmissionRepository(context))
            .ExecuteAsync(new(project.Id), Token);
        Assert.True(workspace.IsSuccess);
        Assert.Equal(ProjectWorkspaceResolutionState.Resolved, workspace.Workspace!.Workflow.ResolutionState);
        Assert.Equal(result.PreQuoteId, workspace.Workspace.Workflow.PreQuoteId);
        Assert.Equal(result.RequirementId, workspace.Workspace.Workflow.RequirementId);
        Assert.Equal("PENDING", workspace.Workspace.Workflow.RequirementStatus);
        Assert.Null(workspace.Workspace.Workflow.TechnicalProposalId);
        Assert.False(workspace.Workspace.Workflow.HasTechnicalProposal);
        var migrations = await context.Database.GetAppliedMigrationsAsync(Token);
        Assert.Contains(migrations, x => x.EndsWith("_AddPublicExperienceRequirementLinks"));
    }

    [Fact]
    public async Task ConcurrentPreparation_CreatesOnePairAndOneCopy()
    {
        var (submission, storage) = await SeedAsync(1);
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var results = await Task.WhenAll(Service(first, storage).ExecuteAsync(submission.SubmissionId, Token),
            Service(second, storage).ExecuteAsync(submission.SubmissionId, Token));
        Assert.All(results, result => Assert.True(result!.IsSuccess, result.ErrorCode));
        Assert.Single(results.Where(x => x!.IsReplay));
        Assert.Equal(results[0]!.PreQuoteId, results[1]!.PreQuoteId);
        Assert.Equal(results[0]!.RequirementId, results[1]!.RequirementId);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(1, await read.PreQuotes.CountAsync(Token));
        Assert.Equal(1, await read.Requirements.CountAsync(Token));
        Assert.Equal(1, await read.RequirementFiles.CountAsync(Token));
        Assert.Equal(2, storage.Files.Count);
    }

    [Fact]
    public async Task NoDocuments_ExplicitError_NoEntitiesOnRepeatedAttempts()
    {
        var (submission, storage) = await SeedAsync(1);
        await using var context = fixture.CreateDbContext();
        // Simulate a historical processed submission without documents.
        await context.Set<PublicExperienceSubmissionFile>()
            .Where(x => x.SubmissionId == submission.Id).ExecuteDeleteAsync(Token);
        submission.Files.Clear();
        storage.Files.Clear();
        var service = Service(context, storage);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await service.ExecuteAsync(submission.SubmissionId, Token);
            Assert.Equal(PublicExperienceRequirementPreparationErrors.DocumentsRequired, result!.ErrorCode);
            await AssertNoPreparedEntitiesAsync(submission, storage, 0);
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task IntegrityFailure_LeavesOriginalsAndNoEntities(string scenario)
    {
        var (submission, storage) = await SeedAsync(1);
        var key = submission.Files.Single().StorageKey;
        storage.Files[key] = scenario switch { "short" => [1], "long" => [1, 2, 3, 4, 5], _ => [9, 9, 9, 9] };
        await using var context = fixture.CreateDbContext();
        var result = await Service(context, storage).ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal(PublicExperienceRequirementPreparationErrors.FileIntegrity, result!.ErrorCode);
        await AssertNoPreparedEntitiesAsync(submission, storage, 1);
        Assert.Equal(1, storage.Writes);
    }

    [Fact]
    public async Task SecondCopyPartialFailure_CompensatesAndCanRetryInSameContext()
    {
        var (submission, storage) = await SeedAsync(2, failOnWrite: 4);
        await using var context = fixture.CreateDbContext();
        var service = Service(context, storage);
        var result = await service.ExecuteAsync(submission.SubmissionId, Token);
        Assert.Equal(PublicExperienceRequirementPreparationErrors.Failed, result!.ErrorCode);
        await AssertNoPreparedEntitiesAsync(submission, storage, 2);
        Assert.Equal(2, storage.Deletes);
        var retry = await service.ExecuteAsync(submission.SubmissionId, Token);
        Assert.True(retry!.IsSuccess, retry.ErrorCode);
        Assert.Equal(4, storage.Files.Count);
        Assert.Equal(1, await context.PreQuotes.CountAsync(Token));
        Assert.Equal(1, await context.Requirements.CountAsync(Token));
    }

    [Fact]
    public async Task DatabaseFailureAfterCopies_CompensatesAndRetryDoesNotDuplicate()
    {
        var (submission, storage) = await SeedAsync(2);
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION core.fail_preparation() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF NEW.requirement_id IS NOT NULL THEN RAISE EXCEPTION 'Injected DB failure'; END IF;
              RETURN NEW;
            END $body$;
            CREATE TRIGGER fail_preparation BEFORE UPDATE ON core.public_experience_submissions
            FOR EACH ROW EXECUTE FUNCTION core.fail_preparation();
            """, Token);
        var service = Service(context, storage);
        try
        {
            var result = await service.ExecuteAsync(submission.SubmissionId, Token);
            Assert.Equal(PublicExperienceRequirementPreparationErrors.Failed, result!.ErrorCode);
            await AssertNoPreparedEntitiesAsync(submission, storage, 2);
            Assert.Equal(2, storage.Deletes);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER fail_preparation ON core.public_experience_submissions;
                DROP FUNCTION core.fail_preparation();
                """, Token);
        }
        var retry = await service.ExecuteAsync(submission.SubmissionId, Token);
        Assert.True(retry!.IsSuccess, retry.ErrorCode);
        Assert.Equal(4, storage.Files.Count);
        Assert.Equal(1, await context.PreQuotes.CountAsync(Token));
        Assert.Equal(1, await context.Requirements.CountAsync(Token));
    }

    [Fact]
    public async Task CancellationDuringCopy_CompensatesWithoutChangingOriginals()
    {
        var (submission, storage) = await SeedAsync(1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancellingStorage = Substitute.For<IFileStorage>();
        cancellingStorage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => storage.OpenReadAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        cancellingStorage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await storage.SaveAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1), Token);
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            });
        cancellingStorage.DeleteIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => storage.DeleteIfExistsAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        await using var context = fixture.CreateDbContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(context, cancellingStorage).ExecuteAsync(submission.SubmissionId, cancellation.Token));
        await AssertNoPreparedEntitiesAsync(submission, storage, 1);
    }

    private async Task<(PublicExperienceSubmission, IntakeStorage)> SeedAsync(int count, int failOnWrite = 0)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var storage = new IntakeStorage { FailOnWrite = failOnWrite };
        await using var context = fixture.CreateDbContext();
        context.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
        await context.SaveChangesAsync(Token);
        var files = Enumerable.Range(0, count).Select(index =>
            PublicExperienceIntakeTests.Upload() with { FileName = $"document-{index}.pdf" }).ToArray();
        var intake = new PublicExperienceIntakeService(new PublicExperienceSubmissionRepository(context),
            storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance);
        var received = await intake.ExecuteAsync(PublicExperienceIntakeTests.Payload(files), files, Token);
        var processed = await new ProcessPublicExperienceSubmissionService(
            new PublicExperienceProcessingRepository(context, TimeProvider.System, NullLogger<PublicExperienceProcessingRepository>.Instance),
            new IdentityRepository(context), new ClientRepository(context), new ProjectRepository(context),
            Options.Create(new PublicExperienceOptions { OwnerEmail = "owner@example.test" }), TimeProvider.System)
            .ExecuteAsync(received.SubmissionId!, Token);
        Assert.Equal("PROCESSED", processed!.Status);
        var submission = await context.Set<PublicExperienceSubmission>().Include(x => x.Files).SingleAsync(Token);
        return (submission, storage);
    }

    private async Task AssertNoPreparedEntitiesAsync(PublicExperienceSubmission original, IntakeStorage storage, int originalCount)
    {
        await using var read = fixture.CreateDbContext();
        Assert.False(await read.PreQuotes.AnyAsync(Token));
        Assert.False(await read.Requirements.AnyAsync(Token));
        Assert.False(await read.RequirementFiles.AnyAsync(Token));
        var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Null(submission.PreQuoteId);
        Assert.Null(submission.RequirementId);
        Assert.Equal("PROCESSED", submission.Status);
        Assert.Equal(original.ProjectId, submission.ProjectId);
        Assert.Equal(original.ClientId, submission.ClientId);
        Assert.Null(submission.ProcessingErrorCode);
        Assert.Equal(originalCount, storage.Files.Count);
        Assert.All(original.Files, file => Assert.True(storage.Files.ContainsKey(file.StorageKey)));
    }

    private static PreparePublicExperienceRequirementService Service(ApplicationDbContext context, IFileStorage storage) =>
        new(new PublicExperienceRequirementPreparationRepository(context, TimeProvider.System,
                NullLogger<PublicExperienceRequirementPreparationRepository>.Instance),
            new ProjectRepository(context), new PreQuoteRepository(context), new RequirementRepository(context),
            storage, TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance);
}
