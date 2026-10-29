using System.Data.Common;
using System.Text.Json;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class ExpiredGate : DbTransactionInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = Signal();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID); await Release.Task.WaitAsync(cancellationToken); return result; }
    }
    private async Task<ExpiredSeed> ExpiredOther(ExpiredSeed first)
    {
        await using var db = fixture.CreateDbContext(); var now = await H.Now();
        var oldQuote = await db.PreQuotes.SingleAsync(x => x.Id == first.Seed.Quote, Token);
        var quote = PreQuote.Create(oldQuote.ProjectId, first.Seed.Actor, "PC-2026-0002", null, now);
        var req = Requirement.Create(quote.Id, first.Seed.Actor, RequirementCommercialLine.Essential, now);
        var job = RequirementProcessingJob.CreateForVerifiedPublication(req.Id, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, now);
        var original = await db.RequirementFiles.FirstAsync(Token);
        var file = RequirementFile.CreateWithStoredEvidence(req.Id, "other.pdf", "application/pdf", 3, "other/not-opened", now,
            original.StoredSha256!, 3, now, 1);
        db.AddRange(quote, req, job, file); await db.SaveChangesAsync(Token);
        var claim = await CombinedClaim().ClaimInitialV2Async(new(job.Id, req.Id, job.ExecutionRequestId, Guid.NewGuid(), Guid.NewGuid()), first.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Claimed, claim.State); var c = claim.Receipt!;
        var e = new AssignedJobExecution(job.Id, job.ExecutionRequestId, req.Id, c.LeaseOwner, 1);
        var started = await new AssignedJobStartStore(H.Options()).StartVerifiedAsync(e, first.Seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.Started, started.State); e = e with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
        var seed = new Seed(e, first.Seed.Actor, quote.Id);
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", seed)).State);
        var expiry = c.AssignedAtUtc.AddTicks(10); Assert.True(await H.Now() > expiry);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={job.Id}", Token);
        var current = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, Token);
        return new(seed, new(new(job.Id, req.Id, job.ExecutionRequestId, c.OperationId), Guid.NewGuid(), c.LeaseOwner,
            1, c.AssignedAtUtc, e.ProcessingAttemptId, current.CancellationRequestedAtUtc!.Value, expiry));
    }
    private async Task<string> ExpiredOwnGraph(ExpiredSeed s)
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new {
            job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Input.Identity.JobId, Token),
            req = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == s.Input.Identity.RequirementId, Token),
            quote = await db.PreQuotes.AsNoTracking().SingleAsync(x => x.Id == s.Seed.Quote, Token),
            attempts = await db.RequirementProcessingAttempts.AsNoTracking().Where(x => x.RequirementId == s.Input.Identity.RequirementId).ToListAsync(Token),
            receipts = await db.ExpiredExecutionCancellations.AsNoTracking().Where(x => x.JobId == s.Input.Identity.JobId).ToListAsync(Token) });
    }

    [Theory]
    [InlineData("same")] [InlineData("different")] [InlineData("collision")]
    public async Task ExpiredB_RacingClosures_LocksAndAtomicCollision(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        var other = mode == "collision" ? await ExpiredOther(s) : s;
        var second = other.Input with { OperationId = mode == "different" ? Guid.NewGuid() : s.Input.OperationId };
        var otherBefore = await ExpiredOwnGraph(other); var immutable = await ClosureImmutable();
        var gate = new ExpiredGate(); var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ExpiredCommand((cmd, _) =>
        {
            if (mode == "collision" ? cmd.CommandText.StartsWith("WITH clock") : cmd.CommandText.Contains("core.pre_quotes") && cmd.CommandText.Contains("FOR UPDATE"))
                reached.TrySetResult(((NpgsqlConnection)cmd.Connection!).ProcessID);
            return Task.CompletedTask;
        });
        var first = ExpiredCloser(gate).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
        Task<ExpiredClosureResult>? next = null;
        try
        {
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            next = ExpiredCloser(probe).CloseExpiredCancellationAsync(second, other.Seed.Actor, Token);
            var loser = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"B {mode} winnerPID={winner} blockedPID={loser}; pg_blocking_pids observed.");
            gate.Release.TrySetResult(); Assert.Equal(JobTerminationState.CancellationConfirmed, (await first).Result.State);
            var result = await next;
            if (mode == "same") Assert.Equal(JobTerminationState.AlreadyRecognized, result.Result.State);
            else ExpiredRejected(result, mode == "collision" ? JobTerminationFailure.IdentityConflict : JobTerminationFailure.TerminalConflict);
            if (mode == "collision") Assert.Equal(otherBefore, await ExpiredOwnGraph(other));
            Assert.Equal(immutable, await ClosureImmutable());
            await using var db = fixture.CreateDbContext(); Assert.Single(await db.ExpiredExecutionCancellations.ToListAsync(Token));
        }
        finally { gate.Release.TrySetResult(); await first; if (next is not null) await next; }
    }

    private ManageRequirementDocumentsService ExpiredReplacement(ApplicationDbContext db, Guid actor, Files files)
    {
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(actor);
        return new(user, new IdentityRepository(db), new RequirementRepository(db), new PreQuoteRepository(db),
            new ProjectRepository(db), new ClientRepository(db), files, TimeProvider.System, NullLogger<ManageRequirementDocumentsService>.Instance);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredB_ReplacementBothOrders_RealOperation_HistoricalReplay(bool closeFirst)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        using var content = new MemoryStream("new"u8.ToArray());
        var gate = new ExpiredGate(); var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ExpiredCommand((cmd, _) =>
        {
            if (cmd.CommandText.Contains("pre_quotes") && cmd.CommandText.Contains("FOR UPDATE")) reached.TrySetResult(((NpgsqlConnection)cmd.Connection!).ProcessID);
            return Task.CompletedTask;
        });
        await using var held = fixture.CreateDbContext();
        IRequirementDocumentWrite? heldWrite = null;
        Task<ExpiredClosureResult>? close = null;
        Task<IRequirementDocumentWrite>? document = null;
        await using var writer = new ApplicationDbContext(H.Options(probe));
        try
        {
            if (closeFirst)
            {
                close = ExpiredCloser(gate).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
                var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                document = new RequirementRepository(writer).BeginDocumentWriteAsync(s.Input.Identity.RequirementId, s.Seed.Actor, Token);
                var loser = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
                output.WriteLine($"B/replacement close-first PID={winner}/{loser}, pg_blocking_pids observed.");
                gate.Release.TrySetResult(); Assert.Equal(JobTerminationState.CancellationConfirmed, (await close).Result.State);
                await using (var scope = await document) Assert.True(scope.Capabilities.CanReplace);
                document = null;
            }
            else
            {
                var denied = await ExpiredReplacement(writer, s.Seed.Actor, files).ReplaceRequirementAsync(new(s.Input.Identity.RequirementId,
                    [new("next.pdf", "application/pdf", 3, content)]), Token);
                Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotReplaceable, denied.Failure);
                // The service rejects in preflight; its real repository scope tests write-protocol serialization.
                heldWrite = await new RequirementRepository(held).BeginDocumentWriteAsync(s.Input.Identity.RequirementId, s.Seed.Actor, Token);
                close = ExpiredCloser(probe).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
                var loser = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                var winner = ((NpgsqlConnection)held.Database.GetDbConnection()).ProcessID;
                await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
                Assert.False(heldWrite.Capabilities.CanReplace);
                output.WriteLine($"B/replacement held policy-first PID={winner}/{loser}, pg_blocking_pids observed; actual operation follows after release.");
                await heldWrite.DisposeAsync(); heldWrite = null;
                Assert.Equal(JobTerminationState.CancellationConfirmed, (await close).Result.State);
            }
        }
        finally
        {
            gate.Release.TrySetResult(); if (heldWrite is not null) await heldWrite.DisposeAsync();
            if (close is not null) await close; if (document is not null) await (await document).DisposeAsync();
        }
        await using (var replacement = fixture.CreateDbContext())
        {
            content.Position = 0;
            var result = await ExpiredReplacement(replacement, s.Seed.Actor, files).ReplaceRequirementAsync(new(s.Input.Identity.RequirementId,
                [new("next.pdf", "application/pdf", 3, content)]), Token);
            Assert.True(result.IsSuccess, result.Failure.ToString());
        }
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await ExpiredCloser(writes).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token)).Result.State);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
        await using var read = fixture.CreateDbContext(); var next = await read.Requirements.SingleAsync(x => x.Id != s.Input.Identity.RequirementId, Token);
        Assert.Equal(s.Input.Identity.RequirementId, next.SupersedesRequirementId); Assert.True(next.IsCurrent);
        Assert.False(await read.RequirementProcessingJobs.AnyAsync(x => x.RequirementId == next.Id, Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.Equal(0, files.Deletes);
    }
}
