using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData("waiting", "Waiting", true)]
    [InlineData("admitted", "Admitted", true)]
    [InlineData("running", "Running", true)]
    [InlineData("request", "CancellationRequested", true)]
    [InlineData("lease-expired", "ContinuityUnknown", false)]
    [InlineData("revoked", "ContinuityUnknown", false)]
    [InlineData("expired", "ContinuityUnknown", false)]
    [InlineData("inactive-project", "ContinuityUnknown", false)]
    [InlineData("cancelled", "Cancelled", false)]
    [InlineData("manual", "Manual", false)]
    [InlineData("manual-job", "Manual", false)]
    public async Task WorkspaceExtraction_ExactState_ReadOnly(string scenario, string expected, bool refresh)
    {
        await using var files = new Files(); var (seed, permit) = await PermitSetup(files); var a = permit.Admission;
        Guid projectId;
        await using (var db = fixture.CreateDbContext()) projectId = (await db.Projects.SingleAsync(Token)).Id;
        if (scenario is "running" or "request" or "lease-expired")
        {
            var claim = await CombinedClaim().ClaimInitialV2Async(new(a.JobId, a.RequirementId, a.ExecutionRequestId,
                a.ClaimOperationId, a.InstanceId) { Permit = permit }, a.ActorUserId, Token);
            Assert.Equal(LeaseOperationState.Claimed, claim.State);
            if (scenario == "lease-expired")
            {
                var expiry = claim.Receipt!.AssignedAtUtc.AddTicks(10); Assert.True(await H.Now() > expiry);
                await using var db = fixture.CreateDbContext();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={a.JobId}", Token);
            }
        }
        if (scenario is "request" or "cancelled")
            await H.Service().RequestJobCancellationAsync(new(a.JobId, a.ExecutionRequestId, a.RequirementId), a.ActorUserId, Token);
        if (scenario == "revoked") await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(a.JobId, a.ActorUserId, Token);
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario == "waiting") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET dispatch_admitted_at_utc=NULL,dispatch_instance_id=NULL,dispatch_claim_operation_id=NULL WHERE id={a.JobId}", Token);
            if (scenario == "manual") await db.RequirementProcessingJobs.Where(x => x.Id == a.JobId).ExecuteDeleteAsync(Token);
            if (scenario == "manual-job") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET automatic_authorization_policy_version=NULL,authorized_actor_user_id=NULL,authorized_at_utc=NULL,authorization_expires_at_utc=NULL,dispatch_admitted_at_utc=NULL,dispatch_instance_id=NULL,dispatch_claim_operation_id=NULL,required_publication_version='sng-job-publication-v1' WHERE id={a.JobId}", Token);
            if (scenario == "expired") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET created_at_utc=created_at_utc-interval '2 days',authorized_at_utc=authorized_at_utc-interval '2 days',authorization_expires_at_utc=authorization_expires_at_utc-interval '2 days',dispatch_admitted_at_utc=dispatch_admitted_at_utc-interval '2 days' WHERE id={a.JobId}", Token);
            if (scenario == "inactive-project") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET is_active=false WHERE id={projectId}", Token);
        }
        var before = await CombinedSnapshot(); var probe = new CombinedCommands();
        var state = await new WorkspaceExtractionReader(H.Options(probe)).ReadAsync(projectId, a.RequirementId, a.ActorUserId, Token);
        Assert.NotNull(state); Assert.Equal(expected, state.State); Assert.Equal(refresh, state.RefreshRecommended);
        Assert.Equal(scenario == "manual", state.CanAnalyze); Assert.Null(state.TechnicalProposalId);
        await using (var db = fixture.CreateDbContext())
        {
            var req = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == a.RequirementId, Token);
            var coherent = await new WorkspaceExtractionReader(H.Options(probe)).ReadCurrentAsync(projectId, req.PreQuoteId, a.ActorUserId, Token);
            Assert.Equal(req.Id, coherent!.RequirementId); Assert.Equal(req.Status.ToString().ToUpperInvariant(), coherent.RequirementStatus);
            Assert.Equal(state, coherent.Extraction);
        }
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(0, files.Opens);
        if (scenario == "manual")
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirements SET commercial_line=NULL WHERE id={a.RequirementId}", Token);
            Assert.False((await new WorkspaceExtractionReader(H.Options()).ReadAsync(projectId, a.RequirementId, a.ActorUserId, Token))!.CanAnalyze);
        }
    }

    [Theory]
    [InlineData("foreign-user")] [InlineData("inactive-user")] [InlineData("foreign-project")] [InlineData("foreign-version")]
    public async Task WorkspaceExtraction_UnauthorizedOrWrongRoute_NoDisclosure(string scenario)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        await using var db = fixture.CreateDbContext(); var project = await db.Projects.SingleAsync(Token);
        if (scenario == "inactive-user") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET is_active=false WHERE id={a.ActorUserId}", Token);
        var before = await CombinedSnapshot(); var probe = new CombinedCommands();
        Assert.Null(await new WorkspaceExtractionReader(H.Options(probe)).ReadAsync(
            scenario == "foreign-project" ? Guid.NewGuid() : project.Id,
            scenario == "foreign-version" ? Guid.NewGuid() : a.RequirementId,
            scenario == "foreign-user" ? Guid.NewGuid() : a.ActorUserId, Token));
        var quote = (await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == a.RequirementId, Token)).PreQuoteId;
        Assert.Null(await new WorkspaceExtractionReader(H.Options(probe)).ReadCurrentAsync(
            scenario == "foreign-project" ? Guid.NewGuid() : project.Id,
            scenario == "foreign-version" ? Guid.NewGuid() : quote,
            scenario == "foreign-user" ? Guid.NewGuid() : a.ActorUserId, Token));
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await CombinedSnapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WorkspaceExtraction_Terminal_ExactProposalAndNoWrites(bool fail)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var copies = new Copies(); var client = new ConsumingClient();
        if (fail) client.Action = (_, _) => throw new IOException("Simulated provider unavailable");
        var calls = SupervisionProbe();
        await new SuperviseAssignedExecutionService(new(calls), PermitCoordinator(files, copies, client),
            new(calls), new(calls), new ManualSupervisionClock()).ExecuteAsync(permit, Token);
        await using var db = fixture.CreateDbContext(); var project = await db.Projects.SingleAsync(Token);
        var before = await CombinedSnapshot(); var probe = new CombinedCommands();
        var state = await new WorkspaceExtractionReader(H.Options(probe)).ReadAsync(project.Id, a.RequirementId, a.ActorUserId, Token);
        Assert.NotNull(state); Assert.Equal(fail ? "Failed" : "Succeeded", state.State);
        Assert.False(state.RefreshRecommended); Assert.False(state.CanAnalyze);
        if (fail) Assert.Null(state.TechnicalProposalId);
        else Assert.Equal((await db.RequirementTechnicalProposals.SingleAsync(Token)).Id, state.TechnicalProposalId);
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await CombinedSnapshot()); Released(files, copies, client);
        if (!fail)
        {
            await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(a.JobId, a.ActorUserId, Token);
            Assert.Equal("Succeeded", (await new WorkspaceExtractionReader(H.Options()).ReadAsync(project.Id, a.RequirementId, a.ActorUserId, Token))!.State);
            // Existing FK rows remain valid, but the publication no longer corresponds to the job correlation.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET correlation_id={Guid.NewGuid()} WHERE id={a.JobId}", Token);
            var inconsistent = await new WorkspaceExtractionReader(H.Options()).ReadAsync(project.Id, a.RequirementId, a.ActorUserId, Token);
            Assert.Equal("Inconsistent", inconsistent!.State); Assert.Null(inconsistent.TechnicalProposalId);
        }
        // Structural version fixture: the old graph must never become the new version's result.
        var previous = await db.Requirements.SingleAsync(x => x.Id == a.RequirementId, Token);
        var now = await H.Now();
        var replacement = Requirement.Create(previous.PreQuoteId, a.ActorUserId, RequirementCommercialLine.Essential, now);
        replacement.MarkAsReplacementOf(previous.Id); previous.SupersedeBy(replacement.Id, now);
        await db.SaveChangesAsync(Token);
        db.Add(replacement); await db.SaveChangesAsync(Token);
        var current = await new RequirementRepository(db).GetCurrentByPreQuoteIdAsync(previous.PreQuoteId, Token);
        Assert.Equal(replacement.Id, current!.RequirementId); Assert.False(current.HasTechnicalProposal);
        var replacementState = await new WorkspaceExtractionReader(H.Options()).ReadAsync(project.Id, replacement.Id, a.ActorUserId, Token);
        Assert.Equal("Manual", replacementState!.State); Assert.Null(replacementState.TechnicalProposalId);
        Assert.False(replacementState.CanAnalyze); // No files were added to this structural version fixture.
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WorkspaceExtraction_CurrentOwnerAndAdmin_ReadNotExecutionAuthority(bool admin)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var next = await NextStartOwner(!admin);
        await using var db = fixture.CreateDbContext(); var project = await db.Projects.SingleAsync(Token);
        if (admin) { var user = await db.Users.SingleAsync(x => x.Id == next, Token); user.ChangeRole(global::Domain.Identity.UserRole.Admin, await H.Now()); await db.SaveChangesAsync(Token); }
        var before = await CombinedSnapshot(); var probe = new CombinedCommands(); var reader = new WorkspaceExtractionReader(H.Options(probe));
        var result = await reader.ReadAsync(project.Id, a.RequirementId, next, Token);
        Assert.Equal(admin ? "Admitted" : "ContinuityUnknown", result!.State); Assert.False(result.CanAnalyze);
        if (!admin) Assert.Null(await reader.ReadAsync(project.Id, a.RequirementId, a.ActorUserId, Token));
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await CombinedSnapshot());
    }

    [Fact]
    public async Task WorkspaceExtraction_ForeignSubmissionLink_InconsistentWithoutDisclosure()
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        await using var db = fixture.CreateDbContext(); var project = await db.Projects.SingleAsync(Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET requirement_id=NULL,pre_quote_id=NULL,requirement_prepared_at_utc=NULL WHERE id={a.SubmissionId}", Token);
        var before = await CombinedSnapshot(); var result = await new WorkspaceExtractionReader(H.Options()).ReadAsync(project.Id, a.RequirementId, a.ActorUserId, Token);
        Assert.Equal("Inconsistent", result!.State); Assert.Null(result.TechnicalProposalId); Assert.False(result.CanAnalyze);
        Assert.Equal(before, await CombinedSnapshot());
    }
}
