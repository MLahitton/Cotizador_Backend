using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.ProcessRequirement;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Operations;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    [Theory]
    [InlineData("job", true)] [InlineData("job", false)]
    [InlineData("legacy", true)] [InlineData("legacy", false)]
    [InlineData("replacement_job", true)] [InlineData("replacement_job", false)]
    [InlineData("replacement", true)]
    [InlineData("remove", true)]
    [InlineData("add_limit", true)]
    [InlineData("add_size", true)]
    public async Task CompetingWrites_ObserveServerLocksAndRevalidate(string scenario, bool documentFirst)
    {
        var seed = scenario is "replacement_job" or "replacement"
            ? await SeedState("cancelled_queued") : await ManualSeed(
                scenario == "remove" ? 2 : scenario == "add_limit" ? 9 : scenario == "add_size" ? 5 : 1,
                totalLimit: scenario == "add_size");
        using var storage = await Files(seed);
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var winnerHook = new Saved(async db =>
        {
            reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            await release.Task.WaitAsync(Token);
        });
        var probe = new JobProtocolTestSupport.QuoteProbe();
        Task<string>? winner = null;
        Task<string>? loser = null;
        async Task<string> Document(params IInterceptor[] hooks)
        {
            await using var db = new ApplicationDbContext(Protocol.H.Options(hooks));
            using var content = new MemoryStream([8, 9]);
            var input = new CreateRequirementFileInput("race.pdf", "application/pdf", 2, content);
            var service = Service(db, seed.Actor, storage);
            RequirementLifecycleResult result;
            if (scenario.StartsWith("replacement", StringComparison.Ordinal))
                result = await service.ReplaceRequirementAsync(new(seed.Execution.RequirementId, [input]), Token);
            else if (scenario == "remove")
            {
                // Choose distinct documents, while both initial reads still see two files.
                var ids = await db.RequirementFiles.OrderBy(f => f.Id).Select(f => f.Id).ToArrayAsync(Token);
                result = await service.RemoveDocumentAsync(new(seed.Execution.RequirementId, hooks.Contains(probe) ? ids[1] : ids[0]), Token);
            }
            else result = await service.AddDocumentAsync(new(seed.Execution.RequirementId, input), Token);
            return result.IsSuccess ? "document" : result.Failure.ToString();
        }
        async Task<string> Other(params IInterceptor[] hooks)
        {
            if (scenario is "replacement" or "remove" or "add_limit" or "add_size") return await Document(hooks);
            await using var db = new ApplicationDbContext(Protocol.H.Options(hooks));
            var repo = new RequirementRepository(db);
            if (scenario == "legacy")
            {
                var provider = Substitute.For<IAi2DocumentProcessingClient>();
                provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
                    .Returns(c => DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(c.Arg<DocumentProcessingClientRequest>())));
                var registry = Substitute.For<IOperationCancellationRegistry>();
                registry.Register(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c => c.Arg<CancellationToken>());
                var core = new ProcessRequirementCoreService(new IdentityRepository(db), new PreQuoteRepository(db),
                    new ProjectRepository(db), new ClientRepository(db), repo, new RequirementProcessingLock(db), storage,
                    provider, registry, PrepareJobPublicationTests.Builder(repo), TimeProvider.System,
                    NullLogger<ProcessRequirementService>.Instance);
                var result = await core.ExecuteAsync(seed.Execution.RequirementId, seed.Actor, Token);
                Assert.True(result.IsSuccess, result.Failure.ToString());
                return "legacy";
            }
            // Test-only enqueue participant: same lock order and Requirement absence lock.
            await using var scope = await repo.BeginDocumentWriteAsync(seed.Execution.RequirementId, seed.Actor, Token);
            if (!scope.Requirement.IsCurrent || scope.Requirement.Status != RequirementStatus.Pending
                || await db.RequirementProcessingAttempts.AnyAsync(a => a.RequirementId == scope.Requirement.Id
                    && (a.ProcessingState == DocumentProcessingState.Pending || a.ProcessingState == DocumentProcessingState.Processing), Token)
                || await db.RequirementProcessingJobs.AnyAsync(j => j.RequirementId == scope.Requirement.Id
                    && (j.State == RequirementProcessingJobState.Queued || j.State == RequirementProcessingJobState.Running), Token))
                return "job_rejected";
            if (scenario == "job" && documentFirst) Assert.Equal(2, scope.Files.Count);
            db.Add(RequirementProcessingJob.Create(scope.Requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
                scenario == "replacement_job" ? RequirementProcessingJobTrigger.ManualRetry : RequirementProcessingJobTrigger.ManualInitial,
                DateTimeOffset.UtcNow, retryOfJobId: scenario == "replacement_job" ? seed.Execution.JobId : null));
            await db.SaveChangesAsync(Token);
            await scope.CommitAsync(Token);
            return "job";
        }
        try
        {
            winner = documentFirst ? Document(winnerHook) : Other(winnerHook);
            var winnerPid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            loser = documentFirst ? Other(probe) : Document(probe);
            var loserPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winnerPid, loserPid);
            await Protocol.WaitBlocked(loserPid, winnerPid);
            output.WriteLine($"{scenario}/{documentFirst}: winner PID {winnerPid}; blocked PID {loserPid}");
            release.TrySetResult();
            var first = await winner;
            var second = await loser;
            Assert.Equal(documentFirst ? "document" : scenario == "legacy" ? "legacy" : "job", first);
            var expected = scenario switch
            {
                "replacement" => "RequirementNotReplaceable",
                "remove" => "InvalidRequest",
                "add_limit" => "TooManyFiles",
                "add_size" => "FileTooLarge",
                "replacement_job" => documentFirst ? "job_rejected" : "RequirementNotReplaceable",
                "job" => documentFirst ? "job" : "RequirementNotMutable",
                _ => documentFirst ? "legacy" : "RequirementNotMutable"
            };
            Assert.Equal(expected, second);
            await using var read = fixture.CreateDbContext();
            Assert.Equal(1, await read.Requirements.CountAsync(r => r.IsActive, Token));
            Assert.Equal(scenario == "replacement" || scenario == "replacement_job" && documentFirst ? 2 : 1,
                await read.Requirements.CountAsync(Token));
            if (scenario == "remove") Assert.Equal(1, await read.RequirementFiles.CountAsync(Token));
            if (scenario == "add_limit") Assert.Equal(10, await read.RequirementFiles.CountAsync(Token));
            if (scenario == "add_size")
            {
                Assert.Equal(6, await read.RequirementFiles.CountAsync(Token));
                Assert.Equal(100L * 1024 * 1024, await read.RequirementFiles.SumAsync(f => f.SizeBytes, Token));
            }
            if (scenario is "job" or "legacy") Assert.Equal(documentFirst ? 2 : 1, await read.RequirementFiles.CountAsync(Token));
            Assert.Equal(0, storage.Deletes);
        }
        finally
        {
            release.TrySetResult();
            if (winner is not null) await winner;
            if (loser is not null) await loser;
        }
    }

    [Theory]
    [InlineData("user", ManageRequirementDocumentsFailure.InactiveUser)]
    [InlineData("project", ManageRequirementDocumentsFailure.InactiveProject)]
    [InlineData("client", ManageRequirementDocumentsFailure.InactiveClient)]
    public async Task ActivityChangesDuringStorage_AreRevalidated(string changed, ManageRequirementDocumentsFailure expected)
    {
        var seed = await ManualSeed();
        using var storage = await Files(seed);
        storage.AfterWrite = async () =>
        {
            await using var update = fixture.CreateDbContext();
            var at = DateTimeOffset.UtcNow;
            if (changed == "user") (await update.Users.SingleAsync(Token)).Deactivate(at);
            if (changed == "project") (await update.Projects.SingleAsync(Token)).SetActive(false, seed.Actor, at);
            if (changed == "client") (await update.Set<Client>().SingleAsync(Token)).SetActive(false, seed.Actor, at);
            await update.SaveChangesAsync(Token);
        };
        await using var db = fixture.CreateDbContext();
        using var content = new MemoryStream([8, 9]);
        var before = await FileSnapshot();
        Assert.Equal(expected, (await Service(db, seed.Actor, storage).AddDocumentAsync(
            new(seed.Execution.RequirementId, new("new.pdf", "application/pdf", 2, content)), Token)).Failure);
        Assert.Equal(before, await FileSnapshot());
        Assert.Equal(1, storage.Writes);
        Assert.Equal(0, storage.Deletes);
        Assert.Equal(2, Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Length);
    }

    private async Task<Seed> ManualSeed(int files = 1, bool totalLimit = false)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        var user = User.CreateFromGoogle("document@test.local", "Owner", null, null, at);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, user.Id, at);
        var project = Project.Create(client.Id, "DOC", "Documents", null, null, user.Id, at);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, at);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, at);
        await using var db = fixture.CreateDbContext();
        db.AddRange(user, client, project, quote, requirement);
        for (var i = 0; i < files; i++)
            db.Add(RequirementFile.Create(requirement.Id, $"file{i}.pdf", "application/pdf",
                totalLimit ? 20 * 1024 * 1024 - (i == files - 1 ? 2 : 0) : 10,
                i == 0 ? "tests/no-read.pdf" : $"tests/file{i}.pdf", at));
        await db.SaveChangesAsync(Token);
        return new(new(Guid.Empty, Guid.Empty, requirement.Id, "none", 1), user.Id, quote.Id);
    }
}
