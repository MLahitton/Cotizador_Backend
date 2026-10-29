using Application.PublicExperience;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Fact]
    public async Task ExperienceEmission_RealPreparationThenExplicitAdmissionAndSupervisor_PublishesOnce()
    {
        fixture.RequireAvailable(); await fixture.ResetAsync();
        await using var files = new Files();
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
            await db.SaveChangesAsync(Token);
            var uploads = new[] { PublicExperienceIntakeTests.Upload("abc"u8.ToArray()) };
            var intake = new PublicExperienceIntakeService(new PublicExperienceSubmissionRepository(db), files,
                TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
            Assert.Equal(201, (await intake.ExecuteAsync(PublicExperienceIntakeTests.Payload(uploads), uploads, Token)).StatusCode);
            var settings = Options.Create(new PublicExperienceOptions { OwnerEmail = "owner@example.test", EmitAutomaticExecution = true });
            var processed = await new ProcessPublicExperienceSubmissionService(new PublicExperienceProcessingRepository(db,
                TimeProvider.System, NullLogger<PublicExperienceProcessingRepository>.Instance), new IdentityRepository(db),
                new ClientRepository(db), new ProjectRepository(db), settings, TimeProvider.System).ExecuteAsync("public-test-id", Token);
            Assert.Equal("PROCESSED", processed!.Status);
            var preparation = new PreparePublicExperienceRequirementService(new PublicExperienceRequirementPreparationRepository(db,
                TimeProvider.System, NullLogger<PublicExperienceRequirementPreparationRepository>.Instance, settings), files,
                PreparePublicExperienceRequirementTests.Copy(), TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance);
            Assert.True((await preparation.ExecuteAsync("public-test-id", Token))!.IsSuccess);
        }
        Guid id;
        await using (var read = fixture.CreateDbContext())
        {
            var job = await read.RequirementProcessingJobs.SingleAsync(Token); id = job.Id;
            Assert.Equal(RequirementProcessingJobState.Queued, job.State);
            Assert.Null(job.DispatchAdmittedAtUtc); Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        }
        var admitted = await new ExperienceAdmissionService(new ExperienceAuthorizationStore(H.Options())).AdmitAsync(id, Token);
        Assert.Equal(ExperienceAdmissionState.Admitted, admitted.State); Assert.NotNull(admitted.Permit);
        var copies = new Copies(); var client = new ConsumingClient();
        var supervisor = new SuperviseAssignedExecutionService(CombinedClaim(), PermitCoordinator(files, copies, client),
            new(new AssignedExecutionObservationStore(H.Options())), new(new ActiveLeaseRenewalStore(H.Options())), TimeProvider.System);
        var result = await supervisor.ExecuteAsync(admitted.Permit, Token);
        Assert.Equal(AssignedSupervisionOutcome.CoordinatorCompleted, result.Outcome);
        await using var final = fixture.CreateDbContext();
        var published = await final.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Succeeded, published.State);
        Assert.Equal(RequirementProcessingJob.PublicationV2, published.PublicationContractVersion);
        Assert.NotNull(published.PublicationContentHash);
        Assert.Single(await final.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.Single(await final.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Equal(1, client.Calls);
        Assert.Null((await new ExperienceAdmissionService(new ExperienceAuthorizationStore(H.Options())).AdmitAsync(id, Token)).Permit);
        Assert.All(client.Readers, r => Assert.False(r.CanRead));
        Assert.All(client.Bytes, b => Assert.Equal("abc"u8.ToArray(), b));
    }
}
