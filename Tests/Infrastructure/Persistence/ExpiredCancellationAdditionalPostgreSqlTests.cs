using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredB_ConfirmAliveWins_OrLosesGuard_NoAutomaticFallback(bool confirmed)
    {
        await using var files = new Files(); var live = await ClosureSetup(files, true);
        var current = await CombinedJob();
        var s = new ExpiredSeed(live.Seed, new(live.Input.Identity, Guid.NewGuid(), live.Input.LeaseOwner, 1,
            live.Input.AssignedAtUtc, live.Input.ExpectedProcessingAttemptId, live.Input.ExpectedCancellationRequestedAtUtc,
            current.LeaseExpiresAtUtc!.Value));
        var before = await ExpiredSnapshot(); var reached = Signal(); var release = Signal();
        var after = new SupervisionAfterCommit(async () => { reached.TrySetResult(); await release.Task.WaitAsync(Token); });
        var saved = new Saved(async db =>
        {
            var expiry = s.Input.AssignedAtUtc.AddTicks(10); Assert.True(await H.Now() > expiry);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry}", Token);
        });
        var a = Closer(write: confirmed ? [after] : [saved]).ConfirmStoppedCancellationAsync(live.Input, live.Drain, s.Seed.Actor, Token);
        if (confirmed)
        {
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                var stable = await ExpiredSnapshot(); var writes = new CombinedCommands();
                ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token), JobTerminationFailure.TerminalConflict);
                Assert.Equal(0, writes.Writes); Assert.Equal(stable, await ExpiredSnapshot()); Assert.False(a.IsCompleted);
            }
            finally { release.TrySetResult(); await a; }
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await a).State);
            await using var read = fixture.CreateDbContext(); Assert.Empty(await read.ExpiredExecutionCancellations.ToListAsync(Token));
        }
        else
        {
            Assert.Equal(JobTerminationFailure.LeaseConflict, (await a).Failure); Assert.Equal(before, await ExpiredSnapshot());
            var expiry = s.Input.AssignedAtUtc.AddTicks(10);
            await using (var prep = fixture.CreateDbContext()) await prep.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry}", Token);
            Assert.True(await H.Now() > expiry);
            var explicitAction = s.Input with { ExpectedLeaseExpiresAtUtc = expiry };
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await ExpiredCloser().CloseExpiredCancellationAsync(explicitAction, s.Seed.Actor, Token)).Result.State);
        }
    }

    [Fact]
    public async Task ExpiredB_WaitsForLock_ClockAfterWait_ExpiresBeforeRelease()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, expired: false);
        var horizon = (await H.Now()).AddSeconds(2);
        await using (var prep = fixture.CreateDbContext()) await prep.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={horizon}", Token);
        Assert.True(await H.Now() < horizon);
        await using var held = fixture.CreateDbContext(); await using var tx = await held.Database.BeginTransactionAsync(Token);
        await held.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE", Token);
        var probe = new JobProtocolTestSupport.QuoteProbe();
        var task = ExpiredCloser(probe).CloseExpiredCancellationAsync(s.Input with { ExpectedLeaseExpiresAtUtc = horizon }, s.Seed.Actor, Token);
        var released = false;
        try
        {
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var blocker = ((NpgsqlConnection)held.Database.GetDbConnection()).ProcessID;
            Assert.NotEqual(pid, blocker); await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocker);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            DateTimeOffset observed;
            while ((observed = await H.Now()) <= horizon) await Task.Delay(20, deadline.Token);
            output.WriteLine($"B DB={observed:O} > expiry={horizon:O} before release PID={blocker}/{pid}; pg_blocking_pids observed.");
            await tx.RollbackAsync(Token); released = true;
            var result = await task; Assert.Equal(JobTerminationState.CancellationConfirmed, result.Result.State);
            Assert.True(result.Receipt!.FinishedAtUtc >= observed); Assert.True(result.Receipt.GuardAtUtc >= result.Receipt.FinishedAtUtc);
        }
        finally { if (!released) await tx.RollbackAsync(Token); await task; }
    }

    [Fact]
    public async Task ExpiredB_ForeignGraphNonempty_Preserved_AndForeignAttemptRejected()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files); var other = await ExpiredOther(s);
        await using (var db = fixture.CreateDbContext())
        {
            var x = RequirementExtractionResult.Create(other.Input.ExpectedProcessingAttemptId!.Value, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, await H.Now());
            db.Add(x); db.Add(RequirementTechnicalProposal.Create(other.Input.Identity.RequirementId, x.Id, other.Input.ExpectedProcessingAttemptId.Value, false, await H.Now()));
            await db.SaveChangesAsync(Token);
        }
        var graph = await ClosureImmutable(); var otherBefore = await ExpiredOwnGraph(other);
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await ExpiredCloser().CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token)).Result.State);
        Assert.Equal(graph, await ClosureImmutable()); Assert.Equal(otherBefore, await ExpiredOwnGraph(other));
    }

    [Theory]
    [InlineData("foreign-attempt")] [InlineData("claim-job")] [InlineData("claim-requirement")]
    public async Task ExpiredB_RelationalIncompatibility_ConstraintsActive(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files); var other = await ExpiredOther(s);
        var e = s.Input;
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "foreign-attempt")
            {
                // The unique attempt link must be released before pointing the target at the foreign attempt.
                // Both jobs remain structurally valid; the store must reject the relational mismatch.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET processing_attempt_id=NULL,attempt_count=0 WHERE id={other.Input.Identity.JobId}", Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET processing_attempt_id={other.Input.ExpectedProcessingAttemptId} WHERE id={e.Identity.JobId}", Token);
                e = e with { ExpectedProcessingAttemptId = other.Input.ExpectedProcessingAttemptId };
            }
            if (mode == "claim-job") e = e with { Identity = e.Identity with { ClaimOperationId = other.Input.Identity.ClaimOperationId }, LeaseOwner = other.Input.LeaseOwner };
            if (mode == "claim-requirement") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET requirement_id={other.Input.Identity.RequirementId} WHERE operation_id={e.Identity.ClaimOperationId}", Token);
        }
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token), mode == "foreign-attempt" ? JobTerminationFailure.AttemptConflict : JobTerminationFailure.IdentityConflict);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
    }

    [Theory]
    [InlineData("operation")] [InlineData("actor")] [InlineData("generation")] [InlineData("owner")]
    [InlineData("attempt")] [InlineData("micro")] [InlineData("offset")]
    public async Task ExpiredB_InvalidInput_NoConnection(string mode)
    {
        var claim = Guid.NewGuid(); var at = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var e = new ExpiredCancellation(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), claim), Guid.NewGuid(),
            $"exec:{Guid.NewGuid():D}:{claim:D}", 1, at, null, at.AddSeconds(1), at.AddSeconds(120));
        var actor = Guid.NewGuid();
        e = mode switch { "operation" => e with { OperationId = Guid.Empty }, "generation" => e with { LeaseGeneration = 2 },
            "owner" => e with { LeaseOwner = "bad" }, "attempt" => e with { ExpectedProcessingAttemptId = Guid.Empty },
            "micro" => e with { AssignedAtUtc = at.AddTicks(1) }, "offset" => e with { AssignedAtUtc = at.ToOffset(TimeSpan.FromHours(1)) }, _ => e };
        var opened = 0;
        ExpiredRejected(await ExpiredCloser(new OpenHook(() => opened++)).CloseExpiredCancellationAsync(e, mode == "actor" ? Guid.Empty : actor, Token), JobTerminationFailure.InvalidRequest);
        Assert.Equal(0, opened);
    }
}
