using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class PreparationBoundary(Func<AssignedJobExecution, Guid, CancellationToken, Task<ExecutionDocumentPreparationResult>> action)
        : IExecutionDocumentPreparation
    {
        public Task<ExecutionDocumentPreparationResult> PrepareAsync(AssignedJobExecution e, Guid actor, CancellationToken ct) => action(e, actor, ct);
    }

    [Fact]
    public async Task Coordinator_ExistingRegistrationWithoutPackage_DoesNotRecreateOrSend()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var otherCopies = new Copies(); var ownCopies = new Copies(); var client = new ConsumingClient();
        var preparation = new PreparationBoundary(async (e, actor, ct) =>
        {
            var other = await Store(files, otherCopies).PrepareAsync(e, actor, ct);
            Assert.Equal(ExecutionDocumentPreparationState.Registered, other.State);
            await other.Package!.DisposeAsync();
            return await Store(files, ownCopies).PrepareAsync(e, actor, ct);
        });
        var result = await Coordinator(files, ownCopies, client, preparation: preparation).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.EvidenceAlreadyRegistered, result.Outcome);
        Assert.Empty(ownCopies.Created); Assert.Equal(2, files.Opens); Assert.Equal(0, client.Calls);
        await AssertRows(1); Released(files, otherCopies, client);
    }

    [Fact]
    public async Task Coordinator_TokenAfterPackageTransfer_DisposesBeforeConsumption()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var copies = new Copies(); var client = new ConsumingClient();
        var preparation = new PreparationBoundary(async (e, actor, ct) =>
        {
            var result = await Store(files, copies).PrepareAsync(e, actor, ct);
            Assert.NotNull(result.Package); caller.Cancel(); return result;
        });
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Coordinator(files, copies, client, preparation: preparation)
            .ExecuteAsync(s.Execution, s.Actor, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken); Assert.Equal(0, client.Calls);
        await AssertRows(1); Released(files, copies, client);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.CancellationRequestedAtUtc);
    }

    [Theory]
    [InlineData("start")] [InlineData("publication")]
    public async Task Coordinator_CallerCancelledAfterCommit_NoLateSuccess(string stage)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var copies = new Copies(); var client = new ConsumingClient(); var injected = false;
        IInterceptor[] hook = [new CommitHook(() => { injected = true; caller.Cancel(); })];
        var closes = new JobProtocolTestSupport.WriteProbe();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Coordinator(files, copies, client,
            start: stage == "start" ? hook : null, publication: stage == "publication" ? hook : null,
            termination: [closes]).ExecuteAsync(s.Execution, s.Actor, caller.Token));
        Assert.True(injected); Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(0, closes.Count);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(stage == "start" ? RequirementProcessingJobState.Running : RequirementProcessingJobState.Succeeded, job.State);
        Assert.Null(job.CancellationRequestedAtUtc);
        Assert.Equal(stage == "start" ? 0 : 1, client.Calls);
        Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_FailureFinalizerRejected_DoesNotAnnounceFailed()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient();
        client.Action = async (_, _) =>
        {
            await using var db = fixture.CreateDbContext();
            var expired = (await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token)).StartedAtUtc!.Value.AddTicks(10);
            Assert.True(await H.Now() > expired);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expired}", Token);
            return DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout);
        };
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal(JobTerminationFailure.LeaseConflict, result.Termination!.Failure);
        await using var read = fixture.CreateDbContext();
        var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.FinishedAtUtc);
        Assert.Null(job.LastErrorCode); Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_NoBusinessLocksDuringCopiesOrProvider()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient(); var checks = 0;
        async Task CheckLocks()
        {
            await using var db = fixture.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Quote} FOR UPDATE NOWAIT", Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirements WHERE id={s.Execution.RequirementId} FOR UPDATE NOWAIT", Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirement_processing_jobs WHERE id={s.Execution.JobId} FOR UPDATE NOWAIT", Token);
            checks++;
            output.WriteLine($"Coordinator I/O lock check {checks}: PID {((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID}");
        }
        copies.AfterCopy = CheckLocks; client.DuringRead = CheckLocks;
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Published, result.Outcome);
        Assert.Equal(3, checks); Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_ContentMismatch_NoUnapprovedFailureCode()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), "xyz"u8.ToArray(), Token);
        var copies = new Copies(); var client = new ConsumingClient(); var closes = new JobProtocolTestSupport.WriteProbe();
        var result = await Coordinator(files, copies, client, termination: [closes]).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionDocumentPreparationFailure.ContentMismatch, result.DocumentFailure);
        Assert.Equal(0, closes.Count); Assert.Equal(0, client.Calls); Assert.Equal(0, files.Deletes);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(RequirementProcessingJobState.Running, (await db.RequirementProcessingJobs.SingleAsync(Token)).State);
        Assert.Empty(await db.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Assert.Equal("xyz"u8.ToArray(), await System.IO.File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData(DocumentProcessingClientFailure.ServiceUnavailable, "AI2_SERVICE_UNAVAILABLE")]
    [InlineData(DocumentProcessingClientFailure.RemoteRejection, "AI2_REQUEST_REJECTED")]
    [InlineData(DocumentProcessingClientFailure.ServiceError, "AI2_SERVICE_ERROR")]
    [InlineData(DocumentProcessingClientFailure.InvalidResponse, "AI_INVALID_RESPONSE")]
    public async Task Coordinator_ProviderFailure_ExistingSafeCodes(DocumentProcessingClientFailure failure, string code)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient
        { Action = (_, _) => Task.FromResult(failure is DocumentProcessingClientFailure.RemoteRejection or DocumentProcessingClientFailure.ServiceError
            ? DocumentProcessingClientResult.RemoteFailure(failure, new(422, "1", "CONTROLLED_ERROR", "Controlled fixture error"))
            : DocumentProcessingClientResult.Failed(failure)) };
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(code, result.Termination!.Receipt!.ErrorCode);
        Assert.Equal(1, client.Calls); Released(files, copies, client);
    }
}
