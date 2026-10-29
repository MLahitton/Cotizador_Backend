using System.Text.Json;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicSubmissionEvidencePostgreSqlTests
{
    private static PublicExperienceIntakeService PhotoService(ApplicationDbContext db, IFileStorage storage) =>
        new(new PublicExperienceSubmissionRepository(db), storage, TimeProvider.System,
            NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture(), new IdeaPhotoCapture());

    [Theory]
    [InlineData("same", 200)]
    [InlineData("name", 200)]
    [InlineData("mime", 200)]
    [InlineData("different", 409)]
    [InlineData("remove", 409)]
    [InlineData("add", 409)]
    [InlineData("historical", 200)]
    [InlineData("document", 409)]
    [InlineData("invalid", 400)]
    public async Task IdeaPhoto_Identity_OriginalEvidenceAndBytesPreserved(string scenario, int status)
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-idea-photo-").FullName;
        try
        {
            var storage = Storage(root);
            var doc = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            var photo = scenario is "add" or "historical" ? null : IdeaPhotoCaptureTests.Upload();
            var json = PublicExperienceIntakeTests.Payload([doc]);
            await using (var db = fixture.CreateDbContext())
                Assert.Equal(201, (await PhotoService(db, storage).ExecuteAsync(json, [doc], photo, Token)).StatusCode);
            string snapshot;
            await using (var db = fixture.CreateDbContext())
            {
                var row = await db.Set<PublicExperienceSubmission>().Include(s => s.Files).Include(s => s.IdeaPhoto).SingleAsync(Token);
                Assert.Equal(Fingerprint, row.PayloadFingerprint);
                Assert.Single(row.Files);
                if (photo is null) Assert.Null(row.IdeaPhoto);
                else
                {
                    Assert.NotNull(row.IdeaPhoto);
                    Assert.Equal(IdeaPhotoCaptureTests.PngHash, row.IdeaPhoto.StoredSha256);
                    Assert.Equal(IdeaPhotoCaptureTests.Png.Length, row.IdeaPhoto.StoredLengthBytes);
                    Assert.Equal("image/png", row.IdeaPhoto.ContentType);
                    Assert.Equal(IdeaPhotoCaptureTests.Png, await File.ReadAllBytesAsync(Path.Combine(root, row.IdeaPhoto.StorageKey), Token));
                }
                snapshot = JsonSerializer.Serialize(row);
            }
            var replayPhoto = scenario switch
            {
                "name" => IdeaPhotoCaptureTests.Upload(name: "renamed.PNG"),
                "mime" => IdeaPhotoCaptureTests.Upload(mime: "image/png"),
                "different" => IdeaPhotoCaptureTests.Upload(IdeaPhotoCaptureTests.Jpeg, "idea.jpg"),
                "remove" or "historical" => null,
                "invalid" => IdeaPhotoCaptureTests.Upload("broken"u8.ToArray()),
                _ => IdeaPhotoCaptureTests.Upload()
            };
            if (scenario == "document") doc = PublicExperienceIntakeTests.Upload("xyz"u8.ToArray());
            await using (var db = fixture.CreateDbContext())
                Assert.Equal(status, (await PhotoService(db, storage).ExecuteAsync(json, [doc], replayPhoto, Token)).StatusCode);
            await using var verify = fixture.CreateDbContext();
            Assert.Equal(snapshot, JsonSerializer.Serialize(await verify.Set<PublicExperienceSubmission>()
                .Include(s => s.Files).Include(s => s.IdeaPhoto).SingleAsync(Token)));
            Assert.Equal(photo is null ? 1 : 2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.False(await verify.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await verify.RequirementProcessingAttempts.AnyAsync(Token));
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    [Theory]
    [InlineData("same")]
    [InlineData("different")]
    [InlineData("absent")]
    public async Task IdeaPhoto_Race_ObservedLock_OnlyWinnerReferenced(string scenario)
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-idea-photo-").FullName;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = new CommitBarrier(release.Task);
        var loser = new InsertProbe();
        Task<PublicExperienceIntakeResult>? a = null, b = null;
        await using var first = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(winner).Options);
        await using var second = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(loser).Options);
        try
        {
            var storage = Storage(root);
            var doc = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            var json = PublicExperienceIntakeTests.Payload([doc]);
            a = PhotoService(first, storage).ExecuteAsync(json, [doc], IdeaPhotoCaptureTests.Upload(), Token);
            var pidA = await winner.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            var photoB = scenario switch
            {
                "absent" => null,
                "different" => IdeaPhotoCaptureTests.Upload(IdeaPhotoCaptureTests.Jpeg, "b.jpg"),
                _ => IdeaPhotoCaptureTests.Upload(name: "b.png")
            };
            b = PhotoService(second, storage).ExecuteAsync(json, [doc], photoB, Token);
            var pidB = await loser.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(pidA, pidB);
            await new JobProtocolTestSupport(fixture).WaitBlocked(pidB, pidA);
            output.WriteLine($"Photo race {scenario}: winner={pidA}; loser={pidB}; Lock and blocker observed");
            release.TrySetResult();
            Assert.Equal(201, (await a).StatusCode);
            Assert.Equal(scenario == "same" ? 200 : 409, (await b).StatusCode);
            await using var read = fixture.CreateDbContext();
            Assert.Equal(1, await read.Set<PublicExperienceSubmission>().CountAsync(Token));
            var photo = await read.Set<PublicExperienceIdeaPhoto>().SingleAsync(Token);
            Assert.Equal("idea.png", photo.OriginalFileName);
            Assert.Equal(IdeaPhotoCaptureTests.PngHash, photo.StoredSha256);
            Assert.Equal(IdeaPhotoCaptureTests.Png, await File.ReadAllBytesAsync(Path.Combine(root, photo.StorageKey), Token));
            Assert.Equal(scenario == "absent" ? 3 : 4, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            release.TrySetResult();
            try { await Task.WhenAll(new[] { a, b }.Where(t => t is not null).Select(t => t!)); }
            finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
        }
    }
}
