using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.ReplaceDocumentFileSafetyPostgreSqlTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class DocumentPersistenceFileSafetyPostgreSqlTests(
    PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly byte[] Original = [1, 2, 3, 4];
    private static readonly byte[] Incoming = [10, 20, 30, 40];

    public static IEnumerable<object[]> ManualCases()
    {
        foreach (var operation in new[] { "create", "add", "remove", "replacement" })
        {
            foreach (var fault in new[] { "normal", "before_save", "persistence", "commit_response", "save_response",
                "cancel_save", "cancel_commit", "cancel_committed", "foreign", "cleanup_failure" })
                yield return [operation, fault];
            if (operation != "create")
                foreach (var fault in new[] { "response", "cancel_response", "lifecycle" }) yield return [operation, fault];
            if (operation != "remove")
                foreach (var fault in new[] { "write_conflict", "write_failure" }) yield return [operation, fault];
            if (operation is "create" or "replacement") yield return [operation, "batch_partial"];
            if (operation == "remove") yield return [operation, "minimum"];
        }
    }

    [Theory]
    [MemberData(nameof(ManualCases))]
    public async Task ManualPaths_PreserveBytesAcrossPersistenceBoundaries(string operation, string fault)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var root = NewRoot();
        var storage = Storage(root, fault);
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        var owner = User.CreateFromGoogle("owner@file.test", "Owner", null, null, at);
        var foreign = User.CreateFromGoogle("foreign@file.test", "Foreign", null, null, at);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, owner.Id, at);
        var project = Project.Create(client.Id, "FILES", "Files", null, null, owner.Id, at);
        var quote = PreQuote.Create(project.Id, owner.Id, "PC-2026-0001", null, at);
        var requirement = Requirement.Create(quote.Id, owner.Id, RequirementCommercialLine.Essential, at);
        var old = RequirementFile.Create(requirement.Id, "old.pdf", "application/pdf", Original.Length, "old/original.pdf", at);
        var sibling = RequirementFile.Create(requirement.Id, "sibling.pdf", "application/pdf", Original.Length, "old/sibling.pdf", at);
        if (operation == "replacement" && fault != "lifecycle") { requirement.StartProcessing(at); requirement.MarkProcessed(at); }
        if (fault == "lifecycle" && operation != "replacement") requirement.Cancel(at);
        var targetQuote = operation == "create" ? PreQuote.Create(project.Id, owner.Id, "PC-2026-0002", null, at) : quote;
        try
        {
            await storage.Inner.SaveAsync(old.StorageKey, new MemoryStream(Original), Token);
            await storage.Inner.SaveAsync(sibling.StorageKey, new MemoryStream(Original), Token);
            await using (var seed = fixture.CreateDbContext())
            {
                seed.AddRange(owner, foreign, client, project, quote, requirement, old);
                if (fault != "minimum") seed.Add(sibling);
                if (targetQuote != quote) seed.Add(targetQuote);
                await seed.SaveChangesAsync(Token);
            }
            var hook = new Faults(fault, cancel);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(hook, new SaveFault(hook), new ResponseFault(hook)).Options;
            bool? success = null;
            var exception = await Record.ExceptionAsync(async () =>
            {
                await using var db = new ApplicationDbContext(options);
                var current = Substitute.For<ICurrentUser>();
                current.IsAuthenticated.Returns(true);
                current.UserId.Returns(fault == "foreign" ? foreign.Id : owner.Id);
                var repo = new RequirementRepository(db);
                using var a = new MemoryStream(Incoming);
                using var b = new MemoryStream(Incoming);
                var input = new CreateRequirementFileInput("new.pdf", "application/pdf", Incoming.Length, a);
                var second = new CreateRequirementFileInput("second.pdf", "application/pdf", Incoming.Length, b);
                if (operation == "create")
                {
                    var service = new CreateRequirementService(new CreateRequirementCommandValidator(), current,
                        new IdentityRepository(db), new PreQuoteRepository(db), new ProjectRepository(db), new ClientRepository(db),
                        repo, storage, TimeProvider.System, NullLogger<CreateRequirementService>.Instance);
                    success = (await service.ExecuteAsync(new(targetQuote.Id, "ESSENTIAL", [input, second]), cancel.Token)).IsSuccess;
                }
                else
                {
                    var service = new ManageRequirementDocumentsService(current, new IdentityRepository(db), repo,
                        new PreQuoteRepository(db), new ProjectRepository(db), new ClientRepository(db), storage,
                        TimeProvider.System, NullLogger<ManageRequirementDocumentsService>.Instance);
                    var result = operation switch
                    {
                        "add" => await service.AddDocumentAsync(new(requirement.Id, input), cancel.Token),
                        "remove" => await service.RemoveDocumentAsync(new(requirement.Id, old.Id), cancel.Token),
                        _ => await service.ReplaceRequirementAsync(new(requirement.Id, [input, second]), cancel.Token)
                    };
                    success = result.IsSuccess;
                    if (fault == "minimum") Assert.Equal(ManageRequirementDocumentsFailure.InvalidRequest, result.Failure);
                    if (success == true) Assert.Equal(operation == "remove" ? 1 : operation == "add" ? 3 : 2, result.Requirement!.FileCount);
                }
            });
            if (fault.StartsWith("cancel_", StringComparison.Ordinal)) Assert.IsAssignableFrom<OperationCanceledException>(exception);
            else { Assert.Null(exception); Assert.Equal(fault is "normal" or "cleanup_failure", success); }
            var committed = fault is "normal" or "cleanup_failure" or "commit_response" or "save_response" or "cancel_committed" or "response" or "cancel_response";
            // SaveChanges now runs inside an explicit document transaction; its lost response precedes commit.
            if (operation != "create" && fault == "save_response") committed = false;
            Assert.Equal(committed, hook.Committed);
            if (fault is "before_save" or "persistence" or "commit_response" or "save_response" or "response" || fault.StartsWith("cancel_")) Assert.True(hook.Injected);
            await using var read = fixture.CreateDbContext();
            var files = await read.RequirementFiles.AsNoTracking().ToListAsync(Token);
            if (fault != "minimum") Assert.Contains(files, f => f.Id == sibling.Id && f.StorageKey == sibling.StorageKey);
            Assert.Equal(Original, await Bytes(storage.Inner, sibling.StorageKey));
            Assert.Equal(!(operation == "remove" && committed), files.Any(f => f.Id == old.Id));
            Assert.Equal(fault == "minimum" ? 1 : committed && operation != "remove" ? (operation == "add" ? 3 : 4) : operation == "remove" && committed ? 1 : 2, files.Count);
            Assert.Equal(Original, await Bytes(storage.Inner, old.StorageKey));
            foreach (var file in files.Where(f => f.Id != old.Id && f.Id != sibling.Id))
            {
                Assert.Equal(Incoming, await Bytes(storage.Inner, file.StorageKey));
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Incoming)), file.StoredSha256);
                Assert.Equal(Incoming.Length, file.StoredLengthBytes);
                Assert.NotNull(file.StoredEvidenceAtUtc);
                Assert.Equal((short)1, file.StoredEvidenceVersion);
            }
            Assert.All(files.Where(f => f.Id == old.Id || f.Id == sibling.Id), f => Assert.Null(f.StoredSha256));
            if (fault is "write_failure" or "batch_partial") Assert.Single(storage.FailedKeys);
            var savedOld = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id == requirement.Id, Token);
            if (operation == "replacement" && committed)
            {
                var next = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id != requirement.Id, Token);
                Assert.Equal(requirement.Id, next.SupersedesRequirementId);
                Assert.Equal(next.Id, savedOld.SupersededByRequirementId);
                Assert.Equal(RequirementStatus.Superseded, savedOld.Status);
            }
            else Assert.Equal(requirement.Status, savedOld.Status);
            Assert.Equal(committed && operation is "create" or "replacement" ? 2 : 1, await read.Requirements.CountAsync(Token));
            if (!committed) Assert.Equal(at.ToUnixTimeMilliseconds(), (await read.PreQuotes.AsNoTracking().SingleAsync(q => q.Id == targetQuote.Id, Token)).UpdatedAtUtc.ToUnixTimeMilliseconds());
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
            Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
            await AssertStorage(storage, root);
            if (fault is "foreign" or "lifecycle" or "minimum") Assert.Empty(storage.Attempts);
            output.WriteLine($"{operation}/{fault}: refs committed={committed}; retained files={Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length}");
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed isolated test storage: {root}"); }
    }

    private string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "sng-d2a-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        output.WriteLine($"Database={fixture.DatabaseName}; storage={path}");
        return path;
    }
    private static BatchStorage Storage(string root, string fault)
    {
        var host = Substitute.For<IHostEnvironment>(); host.ContentRootPath.Returns(root);
        return new(new LocalFileStorage(new FileStorageOptions(root), host), fault);
    }
    private static async Task<byte[]> Bytes(IFileStorage storage, string key)
    {
        await using var stream = await storage.OpenReadAsync(key, Token);
        using var copy = new MemoryStream(); await stream.CopyToAsync(copy, Token); return copy.ToArray();
    }
    private static async Task AssertStorage(BatchStorage storage, string root)
    {
        Assert.Equal(0, storage.Deletes);
        foreach (var pair in storage.Expected) Assert.Equal(pair.Value, await Bytes(storage.Inner, pair.Key));
        foreach (var key in storage.FailedKeys) Assert.False(File.Exists(Path.Combine(root, key)));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }
    private sealed class BatchStorage(IFileStorage inner, string fault) : IFileStorage
    {
        public async Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream content, long expectedLength,
            long maximumLength, CancellationToken cancellationToken)
        {
            Attempts.Add(key);
            if (fault == "write_conflict")
            {
                byte[] foreign = [99, 98, 97];
                using var existing = new MemoryStream(foreign);
                await inner.SaveAsync(key, existing, cancellationToken);
                Expected.Add(key, foreign);
            }
            if (fault == "write_failure" || fault == "batch_partial" && Attempts.Count == 2)
            {
                FailedKeys.Add(key);
                using var broken = new BrokenStream();
                return await inner.SaveVerifiedAsync(key, broken, expectedLength, maximumLength, cancellationToken);
            }
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, cancellationToken);
            bytes.Position = 0;
            var receipt = await inner.SaveVerifiedAsync(key, bytes, expectedLength, maximumLength, cancellationToken);
            Expected.Add(key, bytes.ToArray());
            return receipt;
        }

        internal IFileStorage Inner => inner;
        internal List<string> Attempts { get; } = [];
        internal Dictionary<string, byte[]> Expected { get; } = [];
        internal List<string> FailedKeys { get; } = [];
        internal int Deletes;
        public async Task SaveAsync(string key, Stream content, CancellationToken token)
        {
            Attempts.Add(key);
            if (fault == "write_conflict")
            {
                byte[] foreign = [99, 98, 97];
                await inner.SaveAsync(key, new MemoryStream(foreign), token);
                Expected.Add(key, foreign);
            }
            if (fault == "write_failure" || fault == "batch_partial" && Attempts.Count == 2)
            {
                FailedKeys.Add(key);
                using var broken = new BrokenStream();
                await inner.SaveAsync(key, broken, token);
                return;
            }
            using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, token);
            bytes.Position = 0;
            await inner.SaveAsync(key, bytes, token);
            Expected.Add(key, bytes.ToArray());
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken token) => inner.OpenReadAsync(key, token);
        public Task DeleteIfExistsAsync(string key, CancellationToken token)
        {
            Deletes++;
            if (fault == "cleanup_failure") throw new IOException("Injected delete failure");
            return inner.DeleteIfExistsAsync(key, token);
        }
    }
    private sealed class BrokenStream : MemoryStream
    {
        private bool read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (read) throw new IOException("Injected partial write");
            read = true;
            buffer.Span[0] = 42;
            return ValueTask.FromResult(1);
        }
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        { await destination.WriteAsync(new byte[] { 42 }, cancellationToken); throw new IOException("Injected partial write"); }
    }
}
