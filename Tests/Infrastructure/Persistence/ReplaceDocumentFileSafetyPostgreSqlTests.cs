using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class ReplaceDocumentFileSafetyPostgreSqlTests(
    PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("response", true)]
    [InlineData("commit_response", true)]
    [InlineData("persistence", false)]
    [InlineData("cancel_save", false)]
    [InlineData("cancel_commit", false)]
    [InlineData("cancel_committed", true)]
    [InlineData("cancel_response", true)]
    [InlineData("write_conflict", false)]
    [InlineData("foreign", false)]
    [InlineData("lifecycle", false)]
    public async Task Replace_RetainsPotentiallyReferencedBytes(string scenario, bool committed)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var token = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var root = Path.Combine(Path.GetTempPath(), "sng-replace-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        output.WriteLine($"Scenario={scenario}; database={fixture.DatabaseName}; storage={root}");
        var host = Substitute.For<IHostEnvironment>();
        host.ContentRootPath.Returns(root);
        var storage = new ObservedStorage(new LocalFileStorage(new FileStorageOptions(root), host), scenario);
        byte[] oldBytes = [1, 2, 3, 4];
        byte[] siblingBytes = [5, 6, 7, 8];
        byte[] newBytes = [9, 10, 11, 12];
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var owner = User.CreateFromGoogle("owner@file.test", "Owner", null, null, now);
        var foreign = User.CreateFromGoogle("foreign@file.test", "Foreign", null, null, now);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, owner.Id, now);
        var project = Project.Create(client.Id, "FILES", "Files", null, null, owner.Id, now);
        var quote = PreQuote.Create(project.Id, owner.Id, "PC-2026-0001", null, now);
        var requirement = Requirement.Create(quote.Id, owner.Id, RequirementCommercialLine.Essential, now);
        var old = RequirementFile.Create(requirement.Id, "old.pdf", "application/pdf", oldBytes.Length, "old/original.pdf", now);
        var sibling = RequirementFile.Create(requirement.Id, "sibling.pdf", "application/pdf", siblingBytes.Length, "sibling/original.pdf", now);
        if (scenario == "lifecycle") requirement.Cancel(now.AddSeconds(1));
        var originalStatus = requirement.Status;
        try
        {
            using (var bytes = new MemoryStream(oldBytes)) await storage.Inner.SaveAsync(old.StorageKey, bytes, token);
            using (var bytes = new MemoryStream(siblingBytes)) await storage.Inner.SaveAsync(sibling.StorageKey, bytes, token);
            await using (var seed = fixture.CreateDbContext())
            {
                seed.AddRange(owner, foreign, client, project, quote, requirement, old, sibling);
                await seed.SaveChangesAsync(token);
            }

            var hook = new Faults(scenario, cancellation);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(hook, new ResponseFault(hook), new SaveFault(hook)).Options;
            await using (var db = new ApplicationDbContext(options))
            {
                var currentUser = Substitute.For<ICurrentUser>();
                currentUser.IsAuthenticated.Returns(true);
                currentUser.UserId.Returns(scenario == "foreign" ? foreign.Id : owner.Id);
                var service = new ManageRequirementDocumentsService(currentUser, new IdentityRepository(db),
                    new RequirementRepository(db), new PreQuoteRepository(db), new ProjectRepository(db),
                    new ClientRepository(db), storage, TimeProvider.System,
                    NullLogger<ManageRequirementDocumentsService>.Instance);
                using var content = new MemoryStream(newBytes);
                var command = new ReplaceRequirementDocumentCommand(requirement.Id, old.Id,
                    new CreateRequirementFileInput("new.pdf", "application/pdf", newBytes.Length, content));
                if (scenario.StartsWith("cancel_", StringComparison.Ordinal))
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReplaceDocumentAsync(command, cancellation.Token));
                else
                {
                    var result = await service.ReplaceDocumentAsync(command, cancellation.Token);
                    if (scenario == "normal")
                    {
                        Assert.True(result.IsSuccess);
                        Assert.Equal(requirement.Id, result.Requirement!.RequirementId);
                        Assert.Equal(quote.Id, result.Requirement.PreQuoteId);
                        Assert.Equal("PENDING", result.Requirement.Status);
                        Assert.Equal("ESSENTIAL", result.Requirement.CommercialLine);
                        Assert.Equal(2, result.Requirement.FileCount);
                        Assert.True(result.Requirement.CanEditDocuments);
                        Assert.Contains(result.Requirement.Documents, x => x.FileName == "new.pdf");
                    }
                    else
                    {
                        Assert.False(result.IsSuccess);
                        Assert.Equal(scenario switch
                        {
                            "foreign" => ManageRequirementDocumentsFailure.RequirementNotFound,
                            "lifecycle" => ManageRequirementDocumentsFailure.RequirementNotMutable,
                            _ => ManageRequirementDocumentsFailure.PersistenceError
                        }, result.Failure);
                    }
                }
            }

            // A separate context observes the actual commit/rollback, never tracked state.
            await using var read = fixture.CreateDbContext();
            var files = await read.RequirementFiles.AsNoTracking().Where(x => x.RequirementId == requirement.Id).ToListAsync(token);
            Assert.Equal(2, files.Count);
            var unchanged = Assert.Single(files, x => x.Id == sibling.Id);
            Assert.Equal(sibling.StorageKey, unchanged.StorageKey);
            Assert.Equal(sibling.CreatedAtUtc.ToUnixTimeMilliseconds(), unchanged.CreatedAtUtc.ToUnixTimeMilliseconds());
            Assert.Equal(oldBytes, await Bytes(storage.Inner, old.StorageKey, token));
            Assert.Equal(siblingBytes, await Bytes(storage.Inner, sibling.StorageKey, token));
            Assert.Equal(originalStatus, (await read.Requirements.SingleAsync(x => x.Id == requirement.Id, token)).Status);
            Assert.Equal(0, storage.DeleteCalls);
            Assert.Equal(committed, hook.Committed);
            if (committed)
            {
                Assert.DoesNotContain(files, x => x.Id == old.Id);
                var replacement = Assert.Single(files, x => x.Id != sibling.Id);
                Assert.Equal(storage.AttemptedKey, replacement.StorageKey);
                Assert.Equal(newBytes, await Bytes(storage.Inner, replacement.StorageKey, token));
                Assert.Equal(newBytes.Length, replacement.SizeBytes);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(newBytes)), replacement.StoredSha256);
                Assert.Equal(newBytes.Length, replacement.StoredLengthBytes);
                Assert.NotNull(replacement.StoredEvidenceAtUtc);
                Assert.Equal((short)1, replacement.StoredEvidenceVersion);
            }
            else
            {
                Assert.Contains(files, x => x.Id == old.Id && x.StorageKey == old.StorageKey);
                var activity = (await read.PreQuotes.SingleAsync(x => x.Id == quote.Id, token)).UpdatedAtUtc;
                Assert.Equal(quote.UpdatedAtUtc.ToUnixTimeMilliseconds(), activity.ToUnixTimeMilliseconds());
            }
            if (scenario is "foreign" or "lifecycle")
            {
                Assert.Null(storage.AttemptedKey);
                Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            }
            else
            {
                Assert.NotNull(storage.AttemptedKey);
                Assert.Equal(scenario == "write_conflict" ? ObservedStorage.ExistingBytes : newBytes,
                    await Bytes(storage.Inner, storage.AttemptedKey!, token));
                Assert.Equal(3, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            }
            if (scenario is not ("normal" or "foreign" or "lifecycle" or "write_conflict")) Assert.True(hook.Injected);
        }
        finally
        {
            // Only this test's freshly generated storage root is removed, after contexts/streams close.
            Directory.Delete(root, recursive: true);
            output.WriteLine($"Removed test storage: {root}");
        }
    }

    private static async Task<byte[]> Bytes(IFileStorage storage, string key, CancellationToken token)
    {
        await using var stream = await storage.OpenReadAsync(key, token);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, token);
        return copy.ToArray();
    }

    private sealed class ObservedStorage(IFileStorage inner, string scenario) : IFileStorage
    {
        public async Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream content, long expectedLength,
            long maximumLength, CancellationToken cancellationToken)
        {
            AttemptedKey = key;
            if (scenario == "write_conflict")
            {
                using var existing = new MemoryStream(ExistingBytes);
                await inner.SaveAsync(key, existing, cancellationToken);
            }
            return await inner.SaveVerifiedAsync(key, content, expectedLength, maximumLength, cancellationToken);
        }

        internal static readonly byte[] ExistingBytes = [99, 98, 97];
        internal IFileStorage Inner => inner;
        internal string? AttemptedKey { get; private set; }
        internal int DeleteCalls { get; private set; }
        public async Task SaveAsync(string key, Stream content, CancellationToken token)
        {
            AttemptedKey = key;
            if (scenario == "write_conflict")
            {
                using var existing = new MemoryStream(ExistingBytes);
                await inner.SaveAsync(key, existing, token);
            }
            await inner.SaveAsync(key, content, token);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken token) => inner.OpenReadAsync(key, token);
        public Task DeleteIfExistsAsync(string key, CancellationToken token)
        {
            DeleteCalls++;
            return inner.DeleteIfExistsAsync(key, token);
        }
    }

    internal sealed class Faults(string scenario, CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        internal bool Committed { get; private set; }
        internal bool Injected { get; private set; }
        internal void Inject(string phase)
        {
            if (scenario != phase || Injected) return;
            Injected = true;
            if (phase.StartsWith("cancel_", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            throw new IOException("Injected " + phase);
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Inject("persistence");
            Inject("cancel_commit");
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Committed = true;
            Inject("commit_response");
            Inject("cancel_committed");
            return Task.CompletedTask;
        }
    }

    internal sealed class SaveFault(Faults hook) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            hook.Inject("before_save");
            hook.Inject("cancel_save");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            hook.Inject("save_response");
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class ResponseFault(Faults hook) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (hook.Committed && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                hook.Inject("response");
                hook.Inject("cancel_response");
            }
            return ValueTask.FromResult(result);
        }
    }
}
