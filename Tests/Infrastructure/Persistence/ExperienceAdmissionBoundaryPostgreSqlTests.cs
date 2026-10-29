using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExperienceAuthorizationPostgreSqlTests
{
    private sealed class AdmissionGuardLoses : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("WITH clock"))
                command.CommandText = command.CommandText.Replace("clock.t < j.authorization_expires_at_utc", "clock.t < j.authorized_at_utc");
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Admission_FinalGuardZero_NoPermit_NoClockInference()
    {
        var (_, job) = await AdmissionSetup(); var snapshot = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        var result = await new ExperienceAdmissionService(AdmissionStore(probe, new AdmissionGuardLoses())).AdmitAsync(job.Id, Token);
        Assert.Equal(ExperienceAdmissionState.Rejected, result.State); Assert.Equal(ExperienceAuthorizationFailure.TimeConflict, result.Failure);
        Assert.Null(result.Permit); Assert.Equal(1, probe.Dml); Assert.Equal(snapshot, await AdmissionSnapshot());
    }
    [Theory]
    [InlineData("job")] [InlineData("instance")] [InlineData("claim")]
    [InlineData("revoke-job")] [InlineData("revoke-actor")] [InlineData("service")]
    public async Task Admission_InvalidInput_NoDatabase(string kind)
    {
        var probe = new AdmissionProbe(); var connections = new AdmissionConnectionProbe(); var store = AdmissionStore(probe, connections);
        if (kind.StartsWith("revoke"))
        {
            var result = await new RevokeExperienceAuthorizationService(store).RevokeAsync(
                kind == "revoke-job" ? Guid.Empty : Guid.NewGuid(), kind == "revoke-actor" ? Guid.Empty : Guid.NewGuid(), Token);
            Assert.Equal(ExperienceRevocationState.Rejected, result.State); Assert.Equal(ExperienceAuthorizationFailure.InvalidRequest, result.Failure);
            Assert.Null(result.RevokedAtUtc);
        }
        else if (kind == "service")
        {
            var result = await new ExperienceAdmissionService(store).AdmitAsync(Guid.Empty, Token);
            Assert.Equal(ExperienceAuthorizationFailure.InvalidRequest, result.Failure); Assert.Null(result.Permit);
        }
        else
        {
            var result = await store.AdmitAsync(new(kind == "job" ? Guid.Empty : Guid.NewGuid(),
                kind == "instance" ? Guid.Empty : Guid.NewGuid(), kind == "claim" ? Guid.Empty : Guid.NewGuid()), Token);
            Assert.Equal(ExperienceAdmissionState.Rejected, result.State); Assert.Equal(ExperienceAuthorizationFailure.InvalidRequest, result.Failure);
            Assert.Null(result.Confirmed);
        }
        Assert.Equal(0, probe.Connections); Assert.Equal(0, probe.Dml); Assert.Equal(0, connections.Opened);
    }

    [Theory]
    [InlineData("other", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("admin", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("inactive", ExperienceAuthorizationFailure.InactiveUser)]
    [InlineData("missing", ExperienceAuthorizationFailure.NotFound)]
    [InlineData("route", ExperienceAuthorizationFailure.IdentityConflict)]
    [InlineData("metadata", ExperienceAuthorizationFailure.MetadataConflict)]
    [InlineData("future", ExperienceAuthorizationFailure.NotYetValid)]
    public async Task Admission_RevocationRejections_NoDisclosureOrEffects(string scenario, ExperienceAuthorizationFailure expected)
    {
        var (s, job) = await AdmissionSetup(scenario == "future" ? 86460 : 86400); var actor = s.Actor;
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario is "other" or "admin")
            {
                var other = User.CreateFromGoogle("outsider@example.test", "Other", null, null, DateTimeOffset.UtcNow);
                if (scenario == "admin") other.ChangeRole(UserRole.Admin, DateTimeOffset.UtcNow);
                db.Add(other); await db.SaveChangesAsync(Token); actor = other.Id;
            }
            if (scenario == "inactive") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET is_active=false WHERE id={actor}", Token);
            if (scenario == "route") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET requirement_id={s.OtherRequirement} WHERE id={s.Submission}", Token);
            if (scenario == "metadata") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET automatic_authorization_policy_version=NULL,authorized_actor_user_id=NULL,authorized_at_utc=NULL,authorization_expires_at_utc=NULL WHERE id={job.Id}", Token);
        }
        var before = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        var result = await new RevokeExperienceAuthorizationService(AdmissionStore(probe)).RevokeAsync(scenario == "missing" ? Guid.NewGuid() : job.Id, actor, Token);
        Assert.Equal(ExperienceRevocationState.Rejected, result.State); Assert.Equal(expected, result.Failure); Assert.Null(result.RevokedAtUtc);
        Assert.Equal(scenario == "future" ? 1 : 0, probe.Dml); Assert.Equal(before, await AdmissionSnapshot());
    }

    [Fact]
    public async Task Admission_PriorAttemptRejected_WithConstraintsActive()
    {
        var (s, job) = await AdmissionSetup();
        await using (var db = fixture.CreateDbContext())
        {
            db.Add(RequirementProcessingAttempt.Create(s.Requirement, s.Actor, Guid.NewGuid(), job.CreatedAtUtc));
            await db.SaveChangesAsync(Token);
        }
        var before = await AdmissionSnapshot(); var probe = new AdmissionProbe();
        var result = await new ExperienceAdmissionService(AdmissionStore(probe)).AdmitAsync(job.Id, Token);
        Assert.Equal(ExperienceAdmissionState.Rejected, result.State); Assert.Equal(ExperienceAuthorizationFailure.HistoryConflict, result.Failure);
        Assert.Null(result.Permit); Assert.Equal(0, probe.Dml); Assert.Equal(before, await AdmissionSnapshot());
    }
}
