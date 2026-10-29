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
    [InlineData("claim")] [InlineData("start")] [InlineData("observe")] [InlineData("renew")]
    [InlineData("documents")] [InlineData("consume")] [InlineData("preparation")]
    [InlineData("publication")] [InlineData("termination")]
    public async Task ExperiencePermit_AuthoritativeEntries_AbsentForeignRevoked(string stage)
    {
        foreach (var mode in new[] { "absent", "foreign", "revoked", "inactive", "owner", "dispatch", "expired" })
        {
            await using var files = new Files();
            ExperienceExecutionPermit? foreign = null;
            if (mode == "foreign")
            {
                await using var foreignFiles = new Files();
                (_, foreign) = await PermitSetup(foreignFiles);
            }
            var (_, permit) = await PermitSetup(files, mode == "expired" ? 15 : 86400);
            var a = permit.Admission;
            var claim = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
            var execution = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId,
                $"exec:{a.InstanceId:D}:{a.ClaimOperationId:D}", 1) { Permit = permit };
            var copies = new Copies(); var client = new ConsumingClient();
            var documents = Store(files, copies);
            PreparedExecutionDocuments? package = null;
            VerifiedExecutionDocumentResult? verified = null;
            PreparedJobPublication? publication = null;
            if (stage != "claim")
                Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(claim, a.ActorUserId, Token)).State);
            if (stage is not ("claim" or "start"))
            {
                var started = await H.StartService().StartVerifiedAsync(execution, a.ActorUserId, Token);
                Assert.Equal(AssignedJobStartState.Started, started.State);
                execution = execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
            }
            if (stage is "consume" or "preparation" or "publication")
            {
                var prepared = await documents.PrepareAsync(execution, a.ActorUserId, Token);
                Assert.Equal(ExecutionDocumentPreparationState.Registered, prepared.State);
                package = prepared.Package!;
                if (stage != "consume")
                {
                    var consumed = await Consumer(files, copies, client).ConsumeAsync(package, execution, a.ActorUserId, Token);
                    Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, consumed.State);
                    verified = consumed.Result!;
                    if (stage == "publication")
                    {
                        var builder = new PrepareJobPublicationService(new(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
                            CotizadorBackend.Tests.Application.PreQuotes.PrepareJobPublicationTests.Builder(
                                NSubstitute.Substitute.For<global::Application.Common.Abstractions.PreQuotes.IRequirementRepository>()), documents);
                        publication = await builder.PrepareAsync(verified, RequirementCommercialLine.Essential, Token);
                    }
                }
            }
            if (mode == "revoked")
                Assert.Equal(ExperienceRevocationState.Revoked, (await new RevokeExperienceAuthorizationService(
                    new ExperienceAuthorizationStore(H.Options())).RevokeAsync(a.JobId, a.ActorUserId, Token)).State);
            if (mode == "owner") await NextStartOwner(true);
            if (mode is "inactive" or "dispatch")
            {
                await using var change = fixture.CreateDbContext();
                if (mode == "inactive") await change.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE identity.users SET is_active=false WHERE id={a.ActorUserId}", Token);
                else
                {
                    var other = Guid.NewGuid();
                    await change.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE core.requirement_processing_jobs SET dispatch_instance_id={other} WHERE id={a.JobId}", Token);
                }
            }
            if (mode == "expired")
            {
                var now = await PermitWaitUntilExpired(a.ExpiresAtUtc);
                output.WriteLine($"Permit {stage}: DB={now:O} >= authorization expiry={a.ExpiresAtUtc:O}.");
            }
            var expected = mode == "inactive" ? "InactiveUser" : mode == "owner" ? "NotFound" : "AuthorizationConflict";
            var bad = mode == "absent" ? null : mode == "foreign" ? foreign : permit;
            execution = execution with { Permit = bad };
            claim = claim with { Permit = bad };
            var before = await CombinedSnapshot(); var opens = files.Opens; var calls = client.Calls;
            var writes = new CombinedCommands();
            try
            {
                switch (stage)
                {
                    case "claim":
                        var c = await CombinedClaim(writes).ClaimInitialV2Async(claim, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<LeaseOperationFailure>(expected), c.Failure); Assert.Null(c.Receipt); break;
                    case "start":
                        var s = await new AssignedJobStartStore(H.Options(writes)).StartVerifiedAsync(execution, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<AssignedJobStartFailure>(expected), s.Failure); Assert.Null(s.Receipt); break;
                    case "observe":
                        var o = await new AssignedExecutionObservationStore(H.Options(writes)).ObserveAsync(
                            new(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId) { Permit = bad }, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<LeaseOperationFailure>(expected), o.Failure); Assert.Null(o.Snapshot); break;
                    case "renew":
                        var job = await CombinedJob();
                        var r = await CombinedRenew(writes).RenewActiveV2Async(new(a.JobId, a.RequirementId, a.ExecutionRequestId,
                            Guid.NewGuid(), a.ClaimOperationId, execution.LeaseOwner, 1, job.LeaseExpiresAtUtc!.Value,
                            execution.ProcessingAttemptId) { Permit = bad }, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<LeaseOperationFailure>(expected), r.Failure); Assert.Null(r.Receipt); break;
                    case "documents":
                        var d = await Store(files, copies, writes).PrepareAsync(execution, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<ExecutionDocumentPreparationFailure>(expected), d.Failure); Assert.Null(d.Package); break;
                    case "consume":
                        var v = await Store(files, copies, writes).AuthorizeConsumptionAsync(package!, execution, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<ExecutionDocumentPreparationFailure>(expected), v.Failure); break;
                    case "preparation":
                        var p = await Store(files, copies, writes).AuthorizePreparationAsync(verified!, execution, a.ActorUserId, Token);
                        Assert.Equal(Enum.Parse<ExecutionDocumentPreparationFailure>(expected), p.Failure); break;
                    case "publication":
                        var published = await new JobPublicationStore(H.Options(writes)).PublishAsync(execution, a.ActorUserId, publication!, Token);
                        Assert.Equal(Enum.Parse<JobPublicationFailure>(expected), published.Failure); Assert.Null(published.Receipt); break;
                    default:
                        var t = await new JobTerminationStore(H.Options(writes)).FinalizeExecutionAsync(new(execution), a.ActorUserId, "AI2_TIMEOUT", Token);
                        Assert.Equal(Enum.Parse<JobTerminationFailure>(expected), t.Failure); Assert.Null(t.Receipt); break;
                }
                Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
                Assert.Equal(opens, files.Opens); Assert.Equal(calls, client.Calls);
                output.WriteLine($"Permit {stage}/{mode}: {expected}, no DML/new copies/provider, independent snapshot intact.");
            }
            finally { if (package is not null) await package.DisposeAsync(); }
            Released(files, copies, client);
        }
    }
}
