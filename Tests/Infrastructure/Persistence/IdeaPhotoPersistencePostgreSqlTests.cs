using System.Data.Common;
using System.Text.Json;
using Application.PublicExperience;
using Application.Common.Abstractions.Storage;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicSubmissionEvidencePostgreSqlTests
{
    [Theory]
    [InlineData("document")]
    [InlineData("photo-before")]
    [InlineData("photo-after")]
    [InlineData("receipt")]
    public async Task IdeaPhoto_PartialStorageFailure_NoBusiness_NoDeletion(string scenario)
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-idea-photo-").FullName;
        try
        {
            var real = Storage(root);
            var storage = Substitute.For<IFileStorage>();
            var injected = false;
            storage.SaveVerifiedAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var key = call.Arg<string>()!;
                    if (scenario == "document" || (key.Contains("/idea-photo/") && scenario == "photo-before"))
                    { injected = true; throw new IOException("Injected write failure."); }
                    if (key.Contains("/idea-photo/") && scenario == "receipt")
                    {
                        injected = true;
                        using var other = new MemoryStream("xyz"u8.ToArray());
                        return await real.SaveVerifiedAsync(key, other, 3, IdeaPhotoLimits.MaximumBytes, Token);
                    }
                    var receipt = await real.SaveVerifiedAsync(key, call.Arg<Stream>()!, call.ArgAt<long>(2), call.ArgAt<long>(3), call.Arg<CancellationToken>());
                    if (key.Contains("/idea-photo/") && scenario == "photo-after")
                    { injected = true; throw new IOException("Injected lost storage response."); }
                    return receipt;
                });
            var doc = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            await using var db = fixture.CreateDbContext();
            var error = await Record.ExceptionAsync(() => PhotoService(db, storage).ExecuteAsync(
                PublicExperienceIntakeTests.Payload([doc]), [doc], IdeaPhotoCaptureTests.Upload(), Token));
            if (scenario == "receipt") Assert.IsType<FileStorageWriteException>(error);
            else Assert.IsType<IOException>(error);
            Assert.True(injected);
            await using var read = fixture.CreateDbContext();
            Assert.False(await read.Set<PublicExperienceSubmission>().AnyAsync(Token));
            Assert.False(await read.Set<PublicExperienceSubmissionFile>().AnyAsync(Token));
            Assert.False(await read.Set<PublicExperienceIdeaPhoto>().AnyAsync(Token));
            Assert.Equal(scenario == "document" ? 0 : scenario == "photo-before" ? 1 : 2,
                Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.DoesNotContain(storage.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IFileStorage.DeleteIfExistsAsync));
            await using var first = await new global::Infrastructure.Storage.IdeaPhotoCapture().CaptureAsync(IdeaPhotoCaptureTests.Upload(), Token);
            await using var second = await new global::Infrastructure.Storage.IdeaPhotoCapture().CaptureAsync(IdeaPhotoCaptureTests.Upload(), Token);
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("SubmissionId")]
    [InlineData("OriginalFileName")]
    [InlineData("ContentType")]
    [InlineData("StorageKey")]
    [InlineData("StoredSha256")]
    [InlineData("StoredLengthBytes")]
    [InlineData("StoredEvidenceAtUtc")]
    [InlineData("StoredEvidenceVersion")]
    [InlineData("IdentityVersion")]
    public async Task IdeaPhoto_EfImmutability_NoUpdate_IndependentReread(string property)
    {
        await fixture.ResetAsync();
        await SeedPhotoModel();
        var commands = new Updates();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(commands).Options);
        var photo = await db.Set<PublicExperienceIdeaPhoto>().SingleAsync(Token);
        var snapshot = JsonSerializer.Serialize(photo);
        object value = property switch
        {
            "Id" or "SubmissionId" => Guid.NewGuid(),
            "StoredLengthBytes" => 4L,
            "IdentityVersion" or "StoredEvidenceVersion" => (short)2,
            "StoredEvidenceAtUtc" => At.AddSeconds(1),
            _ => "changed"
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            db.Entry(photo).Property(property).CurrentValue = value;
            await db.SaveChangesAsync(Token);
        });
        Assert.Contains(property, error.Message);
        Assert.Equal(0, commands.Count);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(snapshot, JsonSerializer.Serialize(await read.Set<PublicExperienceIdeaPhoto>().SingleAsync(Token)));
    }

    private async Task SeedPhotoModel()
    {
        await using var db = fixture.CreateDbContext();
        var row = Submission();
        row.AttachIdeaPhoto(PublicExperienceIdeaPhoto.Create(Guid.NewGuid(), row.Id, "a.png", "image/png", "photo/a", Hash, 3, At));
        db.Add(row);
        await db.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task IdeaPhoto_PostgresChecks_Uniques_RestrictiveForeignKey()
    {
        await fixture.ResetAsync();
        await SeedPhotoModel();
        await using var db = fixture.CreateDbContext();
        var photo = await db.Set<PublicExperienceIdeaPhoto>().AsNoTracking().SingleAsync(Token);
        async Task Reject(string sql, string state, string constraint)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
            Assert.Equal(state, error.SqlState);
            Assert.Equal(constraint, error.ConstraintName);
        }
        foreach (var assignment in new[] { "identity_version=2", "stored_evidence_version=0", "stored_length_bytes=0", "stored_length_bytes=20971521", "stored_sha256='bad'", "content_type='image/gif'" })
            await Reject("UPDATE core.public_experience_idea_photos SET " + assignment, PostgresErrorCodes.CheckViolation, "ck_idea_photos_evidence");
        const string fk = "FK_public_experience_idea_photos_public_experience_submissions~";
        await Reject("DELETE FROM core.public_experience_submissions", PostgresErrorCodes.ForeignKeyViolation, fk);
        await Reject("UPDATE core.public_experience_idea_photos SET submission_id='11111111-1111-1111-1111-111111111111'", PostgresErrorCodes.ForeignKeyViolation, fk);
        await Reject("INSERT INTO core.public_experience_idea_photos SELECT gen_random_uuid(),submission_id,original_file_name,content_type,storage_key||'/second',stored_sha256,stored_length_bytes,stored_evidence_at_utc,stored_evidence_version,identity_version FROM core.public_experience_idea_photos",
            PostgresErrorCodes.UniqueViolation, "IX_public_experience_idea_photos_submission_id");
        var other = new PublicExperienceSubmission
        {
            SubmissionId = "other", SchemaVersion = "sng-public-experience-v1", Source = "SNG_WEB",
            ClientFullName = "Client", ClientWhatsapp = "1", AdvisorCode = "AD01", AdvisorDisplayName = "Advisor",
            ProjectName = "Project", ProjectCity = "City", AnswersJson = "{}", PayloadFingerprint = Fingerprint,
            CreatedAtUtc = At, UpdatedAtUtc = At
        };
        db.Add(other); await db.SaveChangesAsync(Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO core.public_experience_idea_photos SELECT gen_random_uuid(),{other.Id},original_file_name,content_type,storage_key,stored_sha256,stored_length_bytes,stored_evidence_at_utc,stored_evidence_version,identity_version FROM core.public_experience_idea_photos", Token));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("IX_public_experience_idea_photos_storage_key", error.ConstraintName);
        Assert.Equal(JsonSerializer.Serialize(photo), JsonSerializer.Serialize(await db.Set<PublicExperienceIdeaPhoto>().AsNoTracking().SingleAsync(Token)));
    }

    [Fact]
    public async Task IdeaPhoto_Migration_PreservesHistoricalFingerprint_NoBackfill_NoPendingModel()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261023091820_AddRequirementExecutionEvidence", Token);
            var row = Submission();
            row.Files.Add(Unknown(row.Id));
            db.Add(row); await db.SaveChangesAsync(Token);
            db.ChangeTracker.Clear();
            var before = JsonSerializer.Serialize(await db.Set<PublicExperienceSubmission>().Include(x => x.Files).AsNoTracking().SingleAsync(Token));
            await migrator.MigrateAsync(cancellationToken: Token);
            Assert.Equal(before, JsonSerializer.Serialize(await db.Set<PublicExperienceSubmission>().Include(x => x.Files).Include(x => x.IdeaPhoto).AsNoTracking().SingleAsync(Token)));
            Assert.False(await db.Set<PublicExperienceIdeaPhoto>().AnyAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IdeaPhoto_CommitFault_RetainsBytes_ExplicitReplay(bool afterCommit, bool cancel)
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-idea-photo-").FullName;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new PhotoCommitFault(afterCommit, cancel ? cts.Cancel : null);
        try
        {
            var storage = Storage(root);
            var doc = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            var json = PublicExperienceIntakeTests.Payload([doc]);
            await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(hook).Options))
            {
                var error = await Record.ExceptionAsync(() => PhotoService(db, storage).ExecuteAsync(json, [doc], IdeaPhotoCaptureTests.Upload(), cts.Token));
                Assert.NotNull(error);
                if (cancel) Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
                else Assert.IsType<IOException>(error);
            }
            Assert.True(hook.Injected);
            await using (var db = fixture.CreateDbContext())
            {
                Assert.Equal(afterCommit ? 1 : 0, await db.Set<PublicExperienceSubmission>().CountAsync(Token));
                Assert.Equal(afterCommit ? 1 : 0, await db.Set<PublicExperienceIdeaPhoto>().CountAsync(Token));
            }
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            if (afterCommit)
            {
                await using var replay = fixture.CreateDbContext();
                Assert.Equal(200, (await PhotoService(replay, storage).ExecuteAsync(json, [doc], IdeaPhotoCaptureTests.Upload(), Token)).StatusCode);
                Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
                var photo = await replay.Set<PublicExperienceIdeaPhoto>().SingleAsync(Token);
                Assert.Equal(IdeaPhotoCaptureTests.Png, await File.ReadAllBytesAsync(Path.Combine(root, photo.StorageKey), Token));
            }
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    private sealed class PhotoCommitFault(bool after, Action? cancel) : DbTransactionInterceptor
    {
        internal bool Injected;
        private void Fail()
        {
            Injected = true;
            cancel?.Invoke();
            if (cancel is not null) throw new OperationCanceledException();
            throw new IOException("Injected photo commit response fault.");
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!after) Fail();
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (after) Fail();
            return Task.CompletedTask;
        }
    }
}
