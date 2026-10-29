using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    private const string AbcDigest = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    [Theory]
    [InlineData("create", false)]
    [InlineData("add", false)]
    [InlineData("replace", false)]
    [InlineData("replacement", false)]
    [InlineData("create", true)]
    [InlineData("add", true)]
    [InlineData("replace", true)]
    [InlineData("replacement", true)]
    public async Task PrivateIncorporation_UsesRealReceipt_AndRejectsLengthMismatch(string operation, bool mismatch)
    {
        var seed = await ManualSeed();
        using var storage = await Files(seed);
        var quoteId = seed.Quote;
        await using (var prepare = fixture.CreateDbContext())
        {
            var requirement = await prepare.Requirements.SingleAsync(Token);
            if (operation == "create")
            {
                var quote = await prepare.PreQuotes.SingleAsync(Token);
                var next = PreQuote.Create(quote.ProjectId, seed.Actor, "PC-2026-0002", null, DateTimeOffset.UtcNow);
                prepare.Add(next);
                quoteId = next.Id;
            }
            if (operation == "replacement")
            {
                // Preserve both unknown and already verified original references.
                using var originalBytes = new MemoryStream("abc"u8.ToArray());
                var receipt = await storage.Inner.SaveVerifiedAsync("old/verified", originalBytes, 3, 3, Token);
                var at = DateTimeOffset.UtcNow;
                prepare.Add(RequirementFile.CreateWithStoredEvidence(requirement.Id, "verified.pdf", "application/pdf", 3,
                    "old/verified", at, receipt.Sha256, receipt.Length, at, 1));
                requirement.StartProcessing(at);
                requirement.MarkProcessed(at);
            }
            await prepare.SaveChangesAsync(Token);
        }
        var before = await FileSnapshot();
        var originalByteFiles = Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Length;
        var completedWriteAt = DateTimeOffset.MinValue;
        storage.AfterWrite = () => { completedWriteAt = DateTimeOffset.UtcNow; return Task.CompletedTask; };
        Guid oldId;
        await using (var read = fixture.CreateDbContext())
            oldId = await read.RequirementFiles.Where(f => f.StorageKey == "tests/no-read.pdf").Select(f => f.Id).SingleAsync(Token);
        var started = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            using var bytes = new MemoryStream("abc"u8.ToArray());
            using var prefix = new MemoryStream("abc"u8.ToArray());
            var input = new CreateRequirementFileInput("incoming.pdf", "application/pdf", mismatch ? 4 : 3, bytes);
            CreateRequirementFileInput[] batch = mismatch
                ? [new("completed.pdf", "application/pdf", 3, prefix), input] : [input];
            if (operation == "create")
            {
                var service = new CreateRequirementService(new CreateRequirementCommandValidator(), Actor(seed.Actor),
                    new IdentityRepository(db), new PreQuoteRepository(db), new ProjectRepository(db), new ClientRepository(db),
                    new RequirementRepository(db), storage, TimeProvider.System, NullLogger<CreateRequirementService>.Instance);
                var result = await service.ExecuteAsync(new(quoteId, "ESSENTIAL", batch), Token);
                Assert.Equal(!mismatch, result.IsSuccess);
                Assert.Equal(mismatch ? CreateRequirementFailure.InvalidRequest : CreateRequirementFailure.None, result.Failure);
            }
            else
            {
                var service = Service(db, seed.Actor, storage);
                var result = operation switch
                {
                    "add" => await service.AddDocumentAsync(new(seed.Execution.RequirementId, input), Token),
                    "replace" => await service.ReplaceDocumentAsync(new(seed.Execution.RequirementId, oldId, input), Token),
                    _ => await service.ReplaceRequirementAsync(new(seed.Execution.RequirementId, batch), Token)
                };
                Assert.Equal(!mismatch, result.IsSuccess);
                Assert.Equal(mismatch ? ManageRequirementDocumentsFailure.InvalidRequest : ManageRequirementDocumentsFailure.None, result.Failure);
            }
        }
        var finished = DateTimeOffset.UtcNow;
        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.RequirementProcessingJobs.AnyAsync(Token));
        Assert.False(await verify.RequirementProcessingAttempts.AnyAsync(Token));
        if (mismatch)
        {
            Assert.Equal(before, await FileSnapshot());
            Assert.Equal(1, await verify.Requirements.CountAsync(Token));
            Assert.Equal(originalByteFiles + (operation is "create" or "replacement" ? 1 : 0),
                Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Length);
        }
        else
        {
            var file = await verify.RequirementFiles.SingleAsync(f => f.OriginalFileName == "incoming.pdf", Token);
            Assert.Equal(AbcDigest, file.StoredSha256);
            Assert.Equal(3, file.StoredLengthBytes);
            Assert.Equal(file.SizeBytes, file.StoredLengthBytes);
            Assert.Equal((short)1, file.StoredEvidenceVersion);
            Assert.InRange(file.StoredEvidenceAtUtc!.Value, started.AddTicks(-10), finished);
            Assert.True(file.StoredEvidenceAtUtc >= completedWriteAt.AddTicks(-10));
            Assert.Equal("abc"u8.ToArray(), await storage.Bytes(file.StorageKey));
            if (operation == "replace") Assert.NotEqual(oldId, file.Id);
            if (operation == "replacement")
            {
                Assert.Equal(before, await FileSnapshot(seed.Execution.RequirementId));
                var successor = await verify.Requirements.SingleAsync(r => r.Id == file.RequirementId, Token);
                Assert.Equal(RequirementStatus.Pending, successor.Status);
                Assert.Equal(seed.Execution.RequirementId, successor.SupersedesRequirementId);
            }
        }
        Assert.Equal(0, storage.Deletes);
    }
}
