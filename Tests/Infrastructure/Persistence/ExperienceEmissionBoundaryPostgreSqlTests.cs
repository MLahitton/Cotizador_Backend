using System.Data.Common;
using Application.PublicExperience;
using Application.Common.Abstractions.Authentication;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicPreparationVerifiedPostgreSqlTests
{
    [Theory]
    [InlineData("evidence")] [InlineData("actor")]
    public async Task ExperienceEmission_InconsistentReplayIsNotRepaired(string kind)
    {
        using var files = await Seed();
        await using var caller = fixture.CreateDbContext();
        Assert.True((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsSuccess);
        await using (var edit = fixture.CreateDbContext())
        {
            if (kind == "evidence")
                await edit.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_sha256=NULL,stored_length_bytes=NULL,stored_evidence_at_utc=NULL,stored_evidence_version=NULL", Token);
            else
            {
                var actor = User.CreateFromGoogle("emission-other@test.local", "Other", null, null, DateTimeOffset.UtcNow);
                edit.Add(actor); await edit.SaveChangesAsync(Token);
                await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET authorized_actor_user_id={actor.Id}", Token);
            }
        }
        var before = await EmissionSnapshot();
        var replay = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.Equal(PublicExperienceRequirementPreparationErrors.InconsistentLinks, replay!.ErrorCode);
        Assert.Equal(before, await EmissionSnapshot()); Assert.Equal(1, files.Saves);
    }

    [Fact]
    public async Task ExperienceEmission_ReplayAfterRealReplacement_PreservesSuccessor()
    {
        using var files = await Seed();
        await using var caller = fixture.CreateDbContext();
        var prepared = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.True(prepared!.IsSuccess);
        Guid actor;
        await using (var edit = fixture.CreateDbContext())
        {
            var job = await edit.RequirementProcessingJobs.SingleAsync(Token);
            actor = job.AuthorizedActorUserId!.Value;
            job.RequestCancellation(job.CreatedAtUtc);
            await edit.SaveChangesAsync(Token);
        }
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(actor);
        await using (var replace = fixture.CreateDbContext())
        {
            var service = new ManageRequirementDocumentsService(user, new IdentityRepository(replace), new RequirementRepository(replace),
                new PreQuoteRepository(replace), new ProjectRepository(replace), new ClientRepository(replace), files,
                TimeProvider.System, NullLogger<ManageRequirementDocumentsService>.Instance);
            using var bytes = new MemoryStream("def"u8.ToArray());
            var result = await service.ReplaceRequirementAsync(new(prepared.RequirementId!.Value,
                [new("next.pdf", "application/pdf", 3, bytes)]), Token);
            Assert.True(result.IsSuccess, result.Failure.ToString());
        }
        var before = await EmissionSnapshot();
        var replay = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.True(replay!.IsSuccess); Assert.True(replay.IsReplay); Assert.Equal(prepared.RequirementId, replay.RequirementId);
        Assert.Equal(before, await EmissionSnapshot());
        await using var read = fixture.CreateDbContext();
        Assert.Single(await read.RequirementProcessingJobs.ToListAsync(Token));
        var successor = await read.Requirements.SingleAsync(r => r.Id != prepared.RequirementId, Token);
        Assert.True(successor.IsCurrent); Assert.Equal(prepared.RequirementId, successor.SupersedesRequirementId);
    }

    [Fact]
    public async Task ExperienceEmission_JobInsertFailure_RollsBackDocumentsAndLinks()
    {
        using var files = await Seed();
        var fault = new EmissionInsertFailure();
        await using var caller = Context(fault);
        var result = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.True(fault.Injected); Assert.False(result!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.Empty(await read.RequirementProcessingJobs.ToListAsync(Token));
        Assert.Empty(await read.PreQuotes.ToListAsync(Token));
        Assert.Empty(await read.Requirements.ToListAsync(Token));
        Assert.Empty(await read.RequirementFiles.ToListAsync(Token));
        var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Null(submission.RequirementId); Assert.Null(submission.PreQuoteId); Assert.Null(submission.RequirementPreparedAtUtc);
        Assert.Equal(2, Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(0, files.Deletes);
    }

    [Fact]
    public async Task ExperienceEmission_OwnerChangedAfterCopy_NoStaleAuthorization()
    {
        using var files = await Seed();
        Guid newOwner = default;
        files.AfterSave = async () =>
        {
            await using var edit = fixture.CreateDbContext();
            var owner = User.CreateFromGoogle("emission-next@test.local", "Next", null, null, DateTimeOffset.UtcNow);
            edit.Add(owner); await edit.SaveChangesAsync(Token); newOwner = owner.Id;
            await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={owner.Id}", Token);
        };
        await using var caller = fixture.CreateDbContext();
        var result = await EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        Assert.False(result!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(newOwner, (await read.Projects.SingleAsync(Token)).CreatedByUserId);
        Assert.Empty(await read.RequirementProcessingJobs.ToListAsync(Token));
        Assert.Empty(await read.Requirements.ToListAsync(Token));
        Assert.Null((await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).RequirementPreparationErrorCode);
    }

    [Fact]
    public async Task ExperienceEmission_ClockTakenAfterObservedLockWait()
    {
        using var files = await Seed();
        var copied = Signal(); var resume = Signal(); var probe = new SubmissionProbe();
        files.AfterSave = async () => { copied.TrySetResult(); await resume.Task.WaitAsync(Token); probe.Enabled = true; };
        await using var caller = Context(probe);
        var task = EmissionService(caller, files).ExecuteAsync("public-test-id", Token);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await copied.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        await using var tx = await connection.BeginTransactionAsync(Token);
        try
        {
            await using var hold = new NpgsqlCommand("SELECT id FROM core.public_experience_submissions FOR UPDATE", connection, tx);
            await hold.ExecuteScalarAsync(Token);
            resume.TrySetResult();
            var blocked = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(connection.ProcessID, blocked);
            await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, connection.ProcessID);
            await using var clock = new NpgsqlCommand("SELECT clock_timestamp()", connection, tx);
            var beforeRelease = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync(Token))!);
            await tx.CommitAsync(Token);
            Assert.True((await task)!.IsSuccess);
            await using var read = fixture.CreateDbContext();
            var job = await read.RequirementProcessingJobs.SingleAsync(Token);
            Assert.True(job.AuthorizedAtUtc >= beforeRelease);
            output.WriteLine($"Emission clock after locks: holder={connection.ProcessID}, blocked={blocked}; pg_blocking_pids observed; DB before release={beforeRelease:O}, Authorized={job.AuthorizedAtUtc:O}.");
        }
        finally { resume.TrySetResult(); await tx.DisposeAsync(); await task; }
    }

    private sealed class EmissionInsertFailure : DbCommandInterceptor
    {
        internal bool Injected;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO core.requirement_processing_jobs", StringComparison.Ordinal))
            { Injected = true; throw new IOException("Job INSERT rejected before commit"); }
            return ValueTask.FromResult(result);
        }
    }
}
