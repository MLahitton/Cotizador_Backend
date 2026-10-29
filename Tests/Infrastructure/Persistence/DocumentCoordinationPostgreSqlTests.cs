using System.Text.Json;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.GetCurrentRequirement;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class DocumentCoordinationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobProtocolTestSupport Protocol => new(fixture);

    [Theory]
    [InlineData("queued", false)]
    [InlineData("running", false)]
    [InlineData("requested", false)]
    [InlineData("expired", false)]
    [InlineData("failed_without_attempt", false)]
    [InlineData("cancelled_queued", true)]
    [InlineData("cancelled_running", true)]
    [InlineData("succeeded", true)]
    [InlineData("failed_attempt", true)]
    [InlineData("mixed", false)]
    [InlineData("additional_attempt", false)]
    public async Task History_FreezesDocumentsAndCancellation_AndControlsReplacement(string state, bool replaceable)
    {
        var seed = await SeedState(state);
        using var storage = await Files(seed);
        var before = await Protocol.P.Snapshot();
        await using var db = fixture.CreateDbContext();
        var repo = new RequirementRepository(db);
        var capabilities = await repo.GetDocumentCapabilitiesAsync(seed.Execution.RequirementId, seed.Actor, Token);
        Assert.Equal(new(false, false, replaceable), capabilities);
        var actor = Actor(seed.Actor);
        var details = await new GetRequirementDetailsService(actor, new IdentityRepository(db), new PreQuoteRepository(db),
            new ProjectRepository(db), new ClientRepository(db), repo).ExecuteAsync(new(seed.Execution.RequirementId), Token);
        Assert.True(details.IsSuccess);
        Assert.False(details.Requirement!.CanEditDocuments);
        Assert.False(details.Requirement.CanCancel);
        Assert.Equal(replaceable, details.Requirement.CanReplace);
        var current = await new GetCurrentRequirementService(actor, new IdentityRepository(db), new PreQuoteRepository(db),
            new ProjectRepository(db), new ClientRepository(db), repo).ExecuteAsync(new(seed.Quote), Token);
        Assert.True(current.IsSuccess);
        Assert.Equal(replaceable, current.Requirement!.CanReplace);
        Assert.False(current.Requirement.CanEditDocuments);
        Assert.False(current.Requirement.CanCancel);
        var file = await db.RequirementFiles.AsNoTracking().SingleAsync(Token);
        var service = Service(db, seed.Actor, storage);
        using var content = new MemoryStream([4, 5, 6]);
        var input = new CreateRequirementFileInput("next.pdf", "application/pdf", 3, content);
        Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotMutable,
            (await service.AddDocumentAsync(new(file.RequirementId, input), Token)).Failure);
        Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotMutable,
            (await service.ReplaceDocumentAsync(new(file.RequirementId, file.Id, input), Token)).Failure);
        Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotMutable,
            (await service.RemoveDocumentAsync(new(file.RequirementId, file.Id), Token)).Failure);
        Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotMutable,
            (await service.CancelAsync(new(file.RequirementId), Token)).Failure);
        if (!replaceable)
            Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotReplaceable,
                (await service.ReplaceRequirementAsync(new(file.RequirementId, [input]), Token)).Failure);
        Assert.Equal(before, await Protocol.P.Snapshot());
        Assert.Equal(0, storage.Writes);
        Assert.Equal(0, storage.Deletes);
    }

    [Theory]
    [InlineData("cancelled_queued")]
    [InlineData("cancelled_running")]
    [InlineData("succeeded")]
    [InlineData("failed_attempt")]
    public async Task Replacement_PreservesHistoricalGraphAndBytes_AndCannotRepeat(string state)
    {
        var seed = await SeedState(state);
        using var storage = await Files(seed);
        await using var before = fixture.CreateDbContext();
        var original = await before.Requirements.AsNoTracking().SingleAsync(Token);
        var originalFiles = await FileSnapshot();
        var graph = await HistoricalGraph();
        using var content = new MemoryStream([4, 5, 6]);
        await using (var db = fixture.CreateDbContext())
        {
            var result = await Service(db, seed.Actor, storage).ReplaceRequirementAsync(
                new(original.Id, [new("next.pdf", "application/pdf", 3, content)]), Token);
            Assert.True(result.IsSuccess, result.Failure.ToString());
            Assert.True(result.Requirement!.CanEditDocuments);
            Assert.True(result.Requirement.CanCancel);
            Assert.False(result.Requirement.CanReplace);
        }
        await using var read = fixture.CreateDbContext();
        var old = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id == original.Id, Token);
        var next = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id != original.Id, Token);
        Assert.Equal(RequirementStatus.Superseded, old.Status);
        Assert.False(old.IsCurrent);
        Assert.Equal(original.CreatedAtUtc, old.CreatedAtUtc);
        Assert.Equal(next.Id, old.SupersededByRequirementId);
        Assert.Equal(old.Id, next.SupersedesRequirementId);
        Assert.Equal(RequirementStatus.Pending, next.Status);
        Assert.True(next.IsCurrent);
        Assert.Equal(original.CommercialLine, next.CommercialLine);
        Assert.Equal(original.CreatedByUserId, next.CreatedByUserId);
        Assert.Equal(1, await read.Requirements.CountAsync(r => r.IsActive, Token));
        Assert.Equal(graph, await HistoricalGraph());
        Assert.Equal(originalFiles, await FileSnapshot(original.Id));
        Assert.False(await read.RequirementProcessingJobs.AnyAsync(j => j.RequirementId == next.Id, Token));
        Assert.False(await read.RequirementProcessingAttempts.AnyAsync(a => a.RequirementId == next.Id, Token));
        Assert.Equal(new byte[10], await storage.Bytes("tests/no-read.pdf"));
        var newFile = await read.RequirementFiles.AsNoTracking().SingleAsync(f => f.RequirementId == next.Id, Token);
        Assert.Equal(new byte[] { 4, 5, 6 }, await storage.Bytes(newFile.StorageKey));
        var stable = await Protocol.P.Snapshot();
        await using var repeat = fixture.CreateDbContext();
        Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotReplaceable,
            (await Service(repeat, seed.Actor, storage).ReplaceRequirementAsync(
                new(original.Id, [new("next.pdf", "application/pdf", 3, content)]), Token)).Failure);
        Assert.Equal(stable, await Protocol.P.Snapshot());
        Assert.Equal(1, storage.Writes);
        Assert.Equal(0, storage.Deletes);
    }

    [Fact]
    public async Task DirtyContext_IsRejectedWithoutDiscardingCallerChanges()
    {
        var seed = await SeedState("cancelled_queued");
        await using var db = fixture.CreateDbContext();
        var quote = await db.PreQuotes.SingleAsync(Token);
        quote.RegisterActivity(DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<RequirementDocumentRejectedException>(() =>
            new RequirementRepository(db).BeginDocumentWriteAsync(seed.Execution.RequirementId, seed.Actor, Token));
        Assert.Equal(ManageRequirementDocumentsFailure.PersistenceError, error.Failure);
        Assert.True(db.ChangeTracker.HasChanges());
    }

    private async Task<Seed> SeedState(string state)
    {
        var seed = await Protocol.H.Create(running: state != "queued" && state != "cancelled_queued");
        if (state is "cancelled_running" or "succeeded" or "failed_attempt" or "mixed" or "additional_attempt")
            seed = await Protocol.Start(seed);
        if (state is "cancelled_queued" or "cancelled_running" or "requested" or "mixed" or "additional_attempt")
        {
            await Protocol.H.Call("request", seed);
            if (state is "cancelled_running" or "mixed" or "additional_attempt")
                Assert.Equal(JobTerminationState.CancellationConfirmed, (await Protocol.H.Call("confirm", seed)).State);
        }
        if (state is "failed_without_attempt" or "failed_attempt") await Protocol.H.Call("fail", seed);
        if (state == "succeeded")
            Assert.Equal(JobPublicationState.Published, (await Protocol.P.Service().PublishAsync(
                seed.Execution, seed.Actor, await Protocol.Prepare(seed), Token)).State);
        if (state is "mixed" or "additional_attempt" or "expired")
        {
            await using var db = fixture.CreateDbContext();
            var now = await Protocol.H.Now();
            if (state == "mixed")
            {
                var job = RequirementProcessingJob.Create(seed.Execution.RequirementId, Guid.NewGuid(), Guid.NewGuid(),
                    RequirementProcessingJobTrigger.ManualRetry, now, retryOfJobId: seed.Execution.JobId);
                job.AssignExecution("fixture", now, now.AddMinutes(1));
                job.Fail("FIXTURE_FAILURE", now);
                db.Add(job);
            }
            else if (state == "additional_attempt")
                db.Add(RequirementProcessingAttempt.Create(seed.Execution.RequirementId, seed.Actor, Guid.NewGuid(), now));
            else
            {
                // Expiry is operational time, not a terminal transition.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {now.AddSeconds(-1)} WHERE id = {seed.Execution.JobId}", Token);
            }
            await db.SaveChangesAsync(Token);
        }
        return seed;
    }

    private async Task<string> HistoricalGraph()
    {
        var all = JsonSerializer.Deserialize<string[]>(await Protocol.P.Snapshot())!;
        using var operational = JsonDocument.Parse(all[0]);
        return JsonSerializer.Serialize(new { jobs = operational.RootElement.GetProperty("jobs"),
            attempts = operational.RootElement.GetProperty("attempts"), graph = all.Skip(1).ToArray() });
    }
    private async Task<string> FileSnapshot(Guid? requirementId = null)
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(await db.RequirementFiles.AsNoTracking()
            .Where(f => requirementId == null || f.RequirementId == requirementId).OrderBy(f => f.Id)
            .Select(f => new { f.Id, f.RequirementId, f.StorageKey, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAtUtc,
                f.StoredSha256, f.StoredLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion })
            .ToArrayAsync(Token));
    }
    private static ICurrentUser Actor(Guid actor)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(actor);
        return user;
    }
    private static ManageRequirementDocumentsService Service(ApplicationDbContext db, Guid actor, IFileStorage storage) =>
        new(Actor(actor), new IdentityRepository(db), new RequirementRepository(db), new PreQuoteRepository(db),
            new ProjectRepository(db), new ClientRepository(db), storage, TimeProvider.System,
            NullLogger<ManageRequirementDocumentsService>.Instance);
    private async Task<TestFiles> Files(Seed seed)
    {
        var files = new TestFiles();
        await using var db = fixture.CreateDbContext();
        foreach (var file in await db.RequirementFiles.Where(f => f.RequirementId == seed.Execution.RequirementId)
                     .Select(f => new { f.StorageKey, f.SizeBytes }).ToArrayAsync(Token))
        {
            using var content = new MemoryStream(new byte[checked((int)file.SizeBytes)]);
            await files.Inner.SaveAsync(file.StorageKey, content, Token);
        }
        output.WriteLine($"Database={fixture.DatabaseName}; storage={files.Root}; requirement={seed.Execution.RequirementId}");
        return files;
    }
    private sealed class TestFiles : IFileStorage, IDisposable
    {
        public async Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream content, long expectedLength,
            long maximumLength, CancellationToken cancellationToken)
        {
            Writes++;
            var receipt = await Inner.SaveVerifiedAsync(key, content, expectedLength, maximumLength, cancellationToken);
            if (AfterWrite is not null) await AfterWrite();
            return receipt;
        }

        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sng-d2b-files-" + Guid.NewGuid().ToString("N"));
        internal IFileStorage Inner { get; }
        internal int Writes;
        internal int Deletes;
        internal Func<Task>? AfterWrite;
        internal TestFiles()
        {
            Directory.CreateDirectory(Root);
            var host = Substitute.For<IHostEnvironment>(); host.ContentRootPath.Returns(Root);
            Inner = new LocalFileStorage(new FileStorageOptions(Root), host);
        }
        public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            Writes++;
            await Inner.SaveAsync(key, content, cancellationToken);
            if (AfterWrite is not null) await AfterWrite();
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) => Inner.OpenReadAsync(key, cancellationToken);
        public Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
        { Deletes++; return Inner.DeleteIfExistsAsync(key, cancellationToken); }
        internal async Task<byte[]> Bytes(string key)
        {
            await using var stream = await Inner.OpenReadAsync(key, Token);
            using var copy = new MemoryStream(); await stream.CopyToAsync(copy, Token); return copy.ToArray();
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
