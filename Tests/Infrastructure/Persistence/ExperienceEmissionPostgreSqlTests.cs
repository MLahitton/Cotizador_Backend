using System.Text.Json;
using Application.PublicExperience;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicPreparationVerifiedPostgreSqlTests
{
    private static PreparePublicExperienceRequirementService EmissionService(ApplicationDbContext db, Files files,
        PublicExperienceOptions? settings = null) => new(new PublicExperienceRequirementPreparationRepository(db,
            TimeProvider.System, NullLogger<PublicExperienceRequirementPreparationRepository>.Instance,
            Options.Create(settings ?? new() { EmitAutomaticExecution = true })), files,
        PreparePublicExperienceRequirementTests.Copy(), TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance);

    private async Task<string> EmissionSnapshot()
    {
        await using var db = fixture.CreateDbContext();
        return (await Graph()) + JsonSerializer.Serialize(await db.RequirementProcessingJobs.AsNoTracking().OrderBy(j => j.Id).ToListAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperienceEmission_NewPreparation_AtomicShapeClockAndCapturedOption(bool enabled)
    {
        using var files = await Seed();
        await using var caller = fixture.CreateDbContext();
        var settings = new PublicExperienceOptions { EmitAutomaticExecution = enabled };
        var service = EmissionService(caller, files, settings);
        files.AfterSave = () => { settings.EmitAutomaticExecution = !enabled; return Task.CompletedTask; };
        var before = await caller.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(Token);
        var original = await Original();
        var result = await service.ExecuteAsync("public-test-id", Token);
        Assert.True(result!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        var after = await read.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(Token);
        var requirement = await read.Requirements.SingleAsync(Token);
        Assert.Equal(RequirementStatus.Pending, requirement.Status);
        Assert.Equal(original, await Original());
        Assert.Equal(enabled ? 1 : 0, await read.RequirementProcessingJobs.CountAsync(Token));
        Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
        Assert.False(await read.JobLeaseOperations.AnyAsync(Token));
        if (enabled)
        {
            var job = await read.RequirementProcessingJobs.SingleAsync(Token);
            Assert.Equal(requirement.Id, job.RequirementId);
            Assert.Equal((await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).Id, job.SubmissionId);
            Assert.Equal(requirement.CreatedByUserId, job.AuthorizedActorUserId);
            Assert.Equal(RequirementProcessingJob.PublicationV2, job.RequiredPublicationVersion);
            Assert.Equal(RequirementProcessingJobTrigger.PublicPreparation, job.TriggerKind);
            Assert.Equal(1, job.AutomaticAuthorizationPolicyVersion);
            Assert.Equal(RequirementProcessingJobState.Queued, job.State);
            Assert.InRange(job.AuthorizedAtUtc!.Value, before, after);
            Assert.Equal(0, job.AuthorizedAtUtc.Value.Ticks % 10);
            Assert.Equal(job.AuthorizedAtUtc.Value.AddHours(24), job.AuthorizationExpiresAtUtc);
            Assert.Equal(job.CreatedAtUtc, job.AuthorizedAtUtc);
            Assert.Null(job.RequestedByUserId); Assert.Null(job.RetryOfJobId);
            Assert.Null(job.DispatchAdmittedAtUtc); Assert.Null(job.DispatchInstanceId); Assert.Null(job.DispatchClaimOperationId);
            Assert.Null(job.AuthorizationRevokedAtUtc); Assert.Null(job.LeaseOwner); Assert.Null(job.StartedAtUtc);
            Assert.Null(job.ProcessingAttemptId); Assert.Equal(0, job.AttemptCount);
        }
        var snapshot = await EmissionSnapshot();
        Assert.True((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsReplay);
        Assert.Equal(snapshot, await EmissionSnapshot());
        Assert.Equal(1, files.Saves); Assert.Equal(0, files.Deletes);
    }

    [Theory]
    [InlineData("admitted")] [InlineData("revoked")] [InlineData("terminal")]
    [InlineData("expired")] [InlineData("historical")]
    public async Task ExperienceEmission_ReplayPreservesHistory_NoBackfill(string state)
    {
        using var files = await Seed();
        await using var caller = fixture.CreateDbContext();
        Assert.True((await EmissionService(caller, files, new() { EmitAutomaticExecution = state != "historical" })
            .ExecuteAsync("public-test-id", Token))!.IsSuccess);
        await using (var edit = fixture.CreateDbContext())
        {
            var job = await edit.RequirementProcessingJobs.SingleOrDefaultAsync(Token);
            if (state == "admitted") Assert.Equal(ExperienceAdmissionState.Admitted,
                (await new ExperienceAdmissionService(new ExperienceAuthorizationStore(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(fixture.ConnectionString).Options)).AdmitAsync(job!.Id, Token)).State);
            if (state == "revoked") job!.RevokeAutomaticAuthorization(job.CreatedAtUtc);
            if (state == "terminal") job!.RequestCancellation(job.CreatedAtUtc);
            if (state == "expired")
                await edit.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET created_at_utc=created_at_utc-interval '2 days', authorized_at_utc=authorized_at_utc-interval '2 days', authorization_expires_at_utc=authorization_expires_at_utc-interval '2 days'", Token);
            else await edit.SaveChangesAsync(Token);
        }
        var before = await EmissionSnapshot();
        foreach (var enabled in new[] { false, true })
        {
            var replay = await EmissionService(caller, files, new() { EmitAutomaticExecution = enabled }).ExecuteAsync("public-test-id", Token);
            Assert.True(replay!.IsSuccess); Assert.True(replay.IsReplay);
            Assert.Equal(before, await EmissionSnapshot());
        }
        Assert.Equal(1, files.Saves); Assert.Equal(0, files.Deletes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperienceEmission_RollbackWholePreparation_KeepFiles(bool documentFailure)
    {
        using var files = await Seed();
        var fault = new SavedFailure();
        if (documentFailure) files.WrongReceipt = "receipt-hash";
        var original = await Original();
        await using var caller = documentFailure ? fixture.CreateDbContext() : Context(fault);
        Assert.False((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.Empty(await read.RequirementProcessingJobs.ToListAsync(Token));
        Assert.Empty(await read.Requirements.ToListAsync(Token));
        Assert.Empty(await read.RequirementFiles.ToListAsync(Token));
        Assert.Empty(await read.PreQuotes.ToListAsync(Token));
        Assert.Equal(original, await Original()); Assert.Equal(0, files.Deletes);
        Assert.Equal(2, Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).Length);
        if (!documentFailure) Assert.True(fault.Injected);
    }

    [Theory]
    [InlineData("project")] [InlineData("user")] [InlineData("client")]
    public async Task ExperienceEmission_RevalidatesContextAfterCopy(string dependency)
    {
        using var files = await Seed();
        files.AfterSave = async () =>
        {
            await using var edit = fixture.CreateDbContext();
            var sql = dependency switch { "project" => "UPDATE core.projects SET is_active=false",
                "client" => "UPDATE core.clients SET is_active=false", _ => "UPDATE identity.users SET is_active=false" };
            await edit.Database.ExecuteSqlRawAsync(sql, Token);
        };
        await using var caller = fixture.CreateDbContext();
        Assert.False((await EmissionService(caller, files).ExecuteAsync("public-test-id", Token))!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.Empty(await read.RequirementProcessingJobs.ToListAsync(Token));
        Assert.Empty(await read.Requirements.ToListAsync(Token));
        Assert.Null((await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).RequirementId);
    }
}
