using System.Text.Json.Nodes;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.ReplaceDocumentFileSafetyPostgreSqlTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentPersistenceFileSafetyPostgreSqlTests
{
    public static IEnumerable<object[]> PublicCases()
    {
        foreach (var operation in new[] { "intake", "prepare" })
            foreach (var fault in new[] { "normal", "before_save", "persistence", "commit_response", "save_response",
                "cancel_save", "cancel_commit", "cancel_committed", "write_conflict", "write_failure", "batch_partial", "cleanup_failure" })
                yield return [operation, fault];
    }

    [Theory]
    [MemberData(nameof(PublicCases))]
    public async Task PublicPaths_PreserveOriginalsAndPotentiallyPublishedCopies(string operation, string fault)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var root = NewRoot();
        var storage = Storage(root, fault);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var uploads = new[] { PublicExperienceIntakeTests.Upload(Incoming), PublicExperienceIntakeTests.Upload(Incoming) with { FileName = "second.pdf" } };
        var payload = PublicExperienceIntakeTests.Payload(uploads);
        var spoolsBefore = Directory.GetDirectories(Path.GetTempPath(), "sng-sealed-*").Order().ToArray();
        PublicExperienceSubmission? original = null;
        try
        {
            if (operation == "prepare")
            {
                await using var seed = fixture.CreateDbContext();
                seed.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
                await seed.SaveChangesAsync(Token);
                Assert.Equal(201, (await Intake(seed, storage.Inner).ExecuteAsync(payload, uploads, Token)).StatusCode);
                await new ProcessPublicExperienceSubmissionService(
                    new PublicExperienceProcessingRepository(seed, TimeProvider.System, NullLogger<PublicExperienceProcessingRepository>.Instance),
                    new IdentityRepository(seed), new ClientRepository(seed), new ProjectRepository(seed),
                    Options.Create(new PublicExperienceOptions { OwnerEmail = "owner@example.test" }), TimeProvider.System)
                    .ExecuteAsync("public-test-id", Token);
                original = await seed.Set<PublicExperienceSubmission>().AsNoTracking().Include(s => s.Files).SingleAsync(Token);
            }
            var hook = new Faults(fault, cancel);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(hook, new SaveFault(hook)).Options;
            bool? success = null;
            var error = await Record.ExceptionAsync(async () =>
            {
                await using var db = new ApplicationDbContext(options);
                if (operation == "intake") success = (await Intake(db, storage).ExecuteAsync(payload, uploads, cancel.Token)).StatusCode == 201;
                else success = (await Preparation(db, storage).ExecuteAsync("public-test-id", cancel.Token))!.IsSuccess;
            });
            if (fault is "normal" or "cleanup_failure" || operation == "prepare" && fault == "commit_response")
            { Assert.Null(error); Assert.True(success); }
            else if (fault.StartsWith("cancel_", StringComparison.Ordinal)) Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if (operation == "prepare" && fault is "before_save" or "save_response" or "write_conflict" or "write_failure" or "batch_partial")
            { Assert.Null(error); Assert.False(success); }
            else { Assert.NotNull(error); Assert.NotEqual(true, success); }
            var committed = fault is "normal" or "cleanup_failure" or "commit_response" or "cancel_committed"
                || operation == "intake" && fault == "save_response";
            if (fault is "before_save" or "persistence" or "commit_response" or "save_response" || fault.StartsWith("cancel_")) Assert.True(hook.Injected);
            await using var read = fixture.CreateDbContext();
            var submissions = await read.Set<PublicExperienceSubmission>().AsNoTracking().Include(s => s.Files).ToListAsync(Token);
            if (operation == "intake" && !committed) Assert.Empty(submissions);
            else
            {
                var saved = Assert.Single(submissions);
                Assert.Equal("public-test-id", saved.SubmissionId);
                Assert.Equal(2, saved.Files.Count);
                foreach (var file in saved.Files)
                {
                    Assert.Equal(Incoming, await Bytes(storage.Inner, file.StorageKey));
                    Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Incoming)), file.ContentHash);
                    Assert.Equal(file.SizeBytes, file.StoredLengthBytes);
                    Assert.NotNull(file.StoredEvidenceAtUtc);
                    Assert.Equal((short)1, file.StoredEvidenceVersion);
                }
                if (original is not null)
                {
                    Assert.Equal(original.Id, saved.Id);
                    Assert.Equal(original.AnswersJson, saved.AnswersJson);
                    Assert.Equal(original.ClientId, saved.ClientId);
                    Assert.Equal(original.ProjectId, saved.ProjectId);
                    Assert.Equal(original.Files.OrderBy(f => f.Id).Select(f => (f.Id, f.StorageKey, f.ContentHash, f.CreatedAtUtc, f.StoredLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion)),
                        saved.Files.OrderBy(f => f.Id).Select(f => (f.Id, f.StorageKey, f.ContentHash, f.CreatedAtUtc, f.StoredLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion)));
                    Assert.Equal(committed, saved.RequirementId is not null && saved.PreQuoteId is not null);
                }
            }
            var requirements = await read.Requirements.AsNoTracking().ToListAsync(Token);
            var files = await read.RequirementFiles.AsNoTracking().ToListAsync(Token);
            if (operation == "prepare" && committed)
            {
                Assert.Equal(RequirementStatus.Pending, Assert.Single(requirements).Status);
                Assert.Equal(2, files.Count);
                foreach (var file in files)
                {
                    Assert.Equal(Incoming, await Bytes(storage.Inner, file.StorageKey));
                    Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Incoming)), file.StoredSha256);
                    Assert.Equal(file.SizeBytes, file.StoredLengthBytes);
                    Assert.NotNull(file.StoredEvidenceAtUtc);
                    Assert.Equal((short)1, file.StoredEvidenceVersion);
                }
                var saved = Assert.Single(submissions);
                Assert.Equal(saved.RequirementId, requirements[0].Id);
                Assert.Equal(saved.PreQuoteId, requirements[0].PreQuoteId);
                Assert.Equal((await read.Projects.AsNoTracking().SingleAsync(Token)).CreatedByUserId, requirements[0].CreatedByUserId);
            }
            else { Assert.Empty(requirements); Assert.Empty(files); }
            Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
            await AssertStorage(storage, root);
            if (fault is "write_failure" or "batch_partial") Assert.Single(storage.FailedKeys);
            if (committed)
            {
                var count = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
                await using var replayDb = fixture.CreateDbContext();
                if (operation == "intake")
                {
                    var intake = Intake(replayDb, storage);
                    Assert.True((await intake.ExecuteAsync(payload, uploads, Token)).IsReplay);
                    var conflict = JsonNode.Parse(payload)!; conflict["answers"]!["uv"] = "no";
                    Assert.Equal(409, (await intake.ExecuteAsync(conflict.ToJsonString(), uploads, Token)).StatusCode);
                }
                else Assert.True((await Preparation(replayDb, storage).ExecuteAsync("public-test-id", Token))!.IsReplay);
                Assert.Equal(count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
                await AssertStorage(storage, root);
            }
            output.WriteLine($"{operation}/{fault}: references committed={committed}; files retained={Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length}");
            Assert.Equal(spoolsBefore, Directory.GetDirectories(Path.GetTempPath(), "sng-sealed-*").Order().ToArray());
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed isolated test storage: {root}"); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task CompetingIntakes_RetainWinnerAndLosingBatch(bool different, bool responseFailure)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var root = NewRoot();
        var aStorage = Storage(root, "normal");
        var bStorage = Storage(root, "normal");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task Barrier()
        {
            if (Interlocked.Increment(ref arrivals) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        }
        try
        {
            await using var a = fixture.CreateDbContext();
            await using var b = fixture.CreateDbContext();
            var aRepo = new RacingIntake(new PublicExperienceSubmissionRepository(a), Barrier, responseFailure);
            var bRepo = new RacingIntake(new PublicExperienceSubmissionRepository(b), Barrier, responseFailure);
            var one = PublicExperienceIntakeTests.Upload(Incoming);
            var two = PublicExperienceIntakeTests.Upload(different ? Original : Incoming);
            var results = new List<PublicExperienceIntakeResult>();
            async Task<Exception?> Run(RacingIntake repo, BatchStorage storage, PublicExperienceUpload upload) =>
                await Record.ExceptionAsync(async () =>
                {
                    var result = await new PublicExperienceIntakeService(repo, storage, TimeProvider.System,
                        NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture()).ExecuteAsync(PublicExperienceIntakeTests.Payload([upload]), [upload], Token);
                    lock (results) results.Add(result);
                });
            var errors = await Task.WhenAll(Run(aRepo, aStorage, one), Run(bRepo, bStorage, two));
            Assert.Single(results, r => r.StatusCode == 201);
            if (responseFailure) Assert.IsType<IOException>(Assert.Single(errors, e => e is not null));
            else { Assert.All(errors, e => Assert.Null(e)); Assert.Contains(results, r => r.StatusCode == (different ? 409 : 200)); }
            await using var read = fixture.CreateDbContext();
            var saved = await read.Set<PublicExperienceSubmission>().AsNoTracking().Include(s => s.Files).SingleAsync(Token);
            var file = Assert.Single(saved.Files);
            var expected = aStorage.Expected.Concat(bStorage.Expected).ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal(expected[file.StorageKey], await Bytes(aStorage.Inner, file.StorageKey));
            await AssertStorage(aStorage, root); await AssertStorage(bStorage, root);
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
        }
        finally { gate.TrySetResult(); Directory.Delete(root, true); output.WriteLine($"Removed isolated test storage: {root}"); }
    }

    private sealed class RacingIntake(IPublicExperienceSubmissionRepository inner, Func<Task> barrier, bool responseFailure)
        : IPublicExperienceSubmissionRepository
    {
        private bool lost;
        public Task<PublicExperienceSubmission?> FindAsync(string id, CancellationToken token) =>
            lost && responseFailure ? throw new IOException("Injected loser recognition failure") : inner.FindAsync(id, token);
        public async Task<bool> TryInsertAsync(PublicExperienceSubmission submission, CancellationToken token)
        { await barrier(); var inserted = await inner.TryInsertAsync(submission, token); lost = !inserted; return inserted; }
    }
    private static PublicExperienceIntakeService Intake(ApplicationDbContext db, IFileStorage storage) =>
        new(new PublicExperienceSubmissionRepository(db), storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
    private static PreparePublicExperienceRequirementService Preparation(ApplicationDbContext db, IFileStorage storage) =>
        new(new PublicExperienceRequirementPreparationRepository(db, TimeProvider.System, NullLogger<PublicExperienceRequirementPreparationRepository>.Instance),
            storage, new global::Infrastructure.Storage.VerifiedContentCopy(NullLogger<global::Infrastructure.Storage.VerifiedContentCopy>.Instance),
            TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance);
}
