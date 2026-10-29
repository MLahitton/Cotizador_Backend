using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData("before")] [InlineData("copy")] [InlineData("provider")]
    [InlineData("package")] [InlineData("response")]
    public async Task Control_Stop_NoLaterStage_ResourcesAndEvidencePreserved(string phase)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        using var control = new AssignedExecutionControl();
        var copies = new Copies(); var client = new ConsumingClient(); var reached = Signal(); var release = Signal();
        var writes = new CombinedCommands();
        async Task Pause() { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); }
        if (phase == "copy") copies.AfterCopy = Pause;
        if (phase == "provider") client.DuringRead = Pause;
        if (phase == "response") client.Action = (r, _) => { control.Stop(); return Task.FromResult(ConsumingClient.Response(r)); };
        IExecutionDocumentPreparation? prepare = phase == "package" ? new PreparationBoundary(async (e, actor, ct) =>
        {
            var result = await Store(files, copies).PrepareAsync(e, actor, ct);
            Assert.NotNull(result.Package); control.Stop(); return result;
        }) : null;
        if (phase == "before") control.Stop();
        var run = Coordinator(files, copies, client, start: phase == "before" ? [writes] : null,
            publication: [writes], termination: [writes], preparation: prepare).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token);
        var failure = Assert.ThrowsAsync<OperationCanceledException>(() => run);
        try
        {
            if (phase is "copy" or "provider")
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                var notification = await control.Started.WaitAsync(TimeSpan.FromSeconds(10), Token);
                Assert.Equal((await CombinedJob()).ProcessingAttemptId, notification!.ProcessingAttemptId);
                Assert.Throws<InvalidOperationException>(control.Dispose);
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(control.Stop, Token)));
                Assert.False(run.IsCompleted); Assert.All(copies.Created, c => Assert.False(c.Disposed));
            }
        }
        finally { release.TrySetResult(); await failure; }
        Assert.Equal(0, writes.Writes); Assert.True(control.IsStopped);
        Assert.Equal(phase is "provider" or "response" ? 1 : 0, client.Calls);
        var job = await CombinedJob();
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.CancellationRequestedAtUtc);
        Assert.Null(job.FinishedAtUtc); Assert.Null(job.PublicationContentHash); Assert.Null(job.LastErrorCode);
        Assert.Equal(phase == "before" ? 0 : 1, job.AttemptCount);
        Assert.Equal(job.ProcessingAttemptId, (await control.Started)?.ProcessingAttemptId);
        Assert.False(control.TryAdmit(AssignedExecutionStage.Termination));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(phase is "provider" or "response" or "package" ? 1 : 0, await db.Set<RequirementExecutionEvidence>().CountAsync(Token));
        Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Empty(await db.RequirementExtractionResults.ToListAsync(Token));
        Released(files, copies, client); await CombinedReceiptUnchanged(s.Receipt);
        Assert.Throws<InvalidOperationException>(() => { _ = Coordinator(files, new Copies(), client).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token); });
        output.WriteLine($"Control Stop {phase}: no later writes, calls={client.Calls}, attempt={job.AttemptCount}, originals intact.");
    }

    [Theory]
    [InlineData("start")] [InlineData("publication")] [InlineData("termination")]
    public async Task Control_StopAfterRealCommit_NoCompensation(string phase)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        using var control = new AssignedExecutionControl(); var copies = new Copies(); var client = new ConsumingClient();
        var injected = false; IInterceptor[] hooks = [new CommitHook(() => { injected = true; control.Stop(); })];
        if (phase == "termination") client.Action = (_, _) => Task.FromResult(DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Coordinator(files, copies, client,
            start: phase == "start" ? hooks : null, publication: phase == "publication" ? hooks : null,
            termination: phase == "termination" ? hooks : null).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token));
        Assert.True(injected);
        var job = await CombinedJob(); Assert.Equal(1, job.AttemptCount);
        Assert.Equal(job.ProcessingAttemptId, (await control.Started)!.ProcessingAttemptId);
        Assert.Equal(phase == "start" ? RequirementProcessingJobState.Running : phase == "publication" ? RequirementProcessingJobState.Succeeded : RequirementProcessingJobState.Failed, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Equal(phase == "start" ? 0 : 1, client.Calls);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(phase == "publication" ? 1 : 0, await db.RequirementTechnicalProposals.CountAsync(Token));
        if (phase == "publication") Assert.Equal(PreparedJobPublication.Version2, job.PublicationContractVersion);
        if (phase == "termination") Assert.Equal("AI2_TIMEOUT", job.LastErrorCode);
        Released(files, copies, client); await CombinedReceiptUnchanged(s.Receipt);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Control_NoStartedNotification_ForReplayOrUncertainStart(bool uncertain)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        using var control = new AssignedExecutionControl(); var copies = new Copies(); var client = new ConsumingClient();
        var lost = false;
        if (!uncertain) Assert.Equal(AssignedJobStartState.Started, (await H.StartService().StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token)).State);
        IInterceptor[] hooks = [new CommitHook(() => { lost = true; throw new IOException("lost"); }),
            new OpenHook(() => { if (lost) throw new IOException("observer unavailable"); })];
        var result = await Coordinator(files, copies, client, start: uncertain ? hooks : null).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token);
        Assert.Equal(uncertain ? AssignedExecutionOutcome.Indeterminate : AssignedExecutionOutcome.AlreadyStarted, result.Outcome);
        Assert.Null(await control.Started); Assert.Empty(copies.Created); Assert.Equal(0, files.Opens); Assert.Equal(0, client.Calls);
        Assert.Equal(1, (await CombinedJob()).AttemptCount); Assert.False(control.TryAdmit(AssignedExecutionStage.Documents));
        Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Control_BusinessCancellationVersusCaller_OriginalToken(bool business)
    {
        await using var files = new Files(); var s = await CombinedSetup(files); var copies = new Copies();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token); using var control = new AssignedExecutionControl();
        var client = new ConsumingClient { Action = async (r, _) =>
        {
            if (business) Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(s.Seed))).State);
            else { control.Stop(); caller.Cancel(); }
            return ConsumingClient.Response(r);
        }};
        var task = Coordinator(files, copies, client).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, caller.Token);
        if (business) Assert.Equal(AssignedExecutionOutcome.Cancelled, (await task).Outcome);
        else Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
        var job = await CombinedJob();
        Assert.Equal(business ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Running, job.State);
        Assert.Equal(business, job.CancellationRequestedAtUtc.HasValue);
        Assert.Equal(1, client.Calls); Assert.NotNull(await control.Started); Released(files, copies, client);
    }

    [Fact]
    public async Task Control_SucceededObservationAndLateStop_PreservesConfirmedResult()
    {
        await using var files = new Files(); var s = await CombinedSetup(files); var copies = new Copies(); var client = new ConsumingClient();
        using var control = new AssignedExecutionControl();
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token);
        Assert.Equal(AssignedExecutionOutcome.Published, result.Outcome);
        var before = await CombinedSnapshot(); control.Stop(); control.Stop();
        Assert.Equal(before, await CombinedSnapshot()); Assert.Equal(AssignedExecutionOutcome.Published, result.Outcome);
        var writes = new CombinedCommands(); var observer = new ObserveAssignedExecutionService(new AssignedExecutionObservationStore(H.Options(writes)));
        var observed = await observer.ObserveAsync(new(s.Request.JobId, s.Request.RequirementId, s.Request.ExecutionRequestId, s.Request.OperationId), s.Seed.Actor, Token);
        Assert.Equal(AssignedExecutionObservationState.Observed, observed.State);
        Assert.True(observed.Snapshot!.IsTerminal); Assert.Equal(RequirementProcessingJobState.Succeeded, observed.Snapshot.JobState);
        Assert.Equal(ObservedExecutionLease.Released, observed.Snapshot.Lease); Assert.Equal(0, writes.Writes);
        Assert.Equal(before, await CombinedSnapshot()); Assert.Equal((await CombinedJob()).ProcessingAttemptId, (await control.Started)!.ProcessingAttemptId);
        Released(files, copies, client);
    }

    [Fact]
    public async Task Control_StopWithOutstandingRead_DrainsBeforeDisposing()
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        using var control = new AssignedExecutionControl(); var copies = new BlockingReadCopies(); var client = new StopWithReadClient(control);
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);
        var coordinator = new CoordinateAssignedExecutionService(new(new AssignedJobStartStore(H.Options())), store,
            new(store, client, NullLogger<ConsumeExecutionDocumentsService>.Instance),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>())),
            new(new JobPublicationStore(H.Options())), H.Service());
        var task = coordinator.ExecuteAsync(s.Seed.Execution, s.Seed.Actor, control, Token);
        var failure = Assert.ThrowsAsync<OperationCanceledException>(() => task);
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (client.Reader is null || client.Reader.CanRead) { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
            Assert.True(control.IsStopped); Assert.False(task.IsCompleted); Assert.Equal(0, copies.Disposals);
        }
        finally { copies.Release.TrySetResult(); await failure; if (client.Read is not null) await client.Read; }
        Assert.Equal(2, copies.Disposals); Assert.Equal(1, client.Calls); Assert.Equal(0, files.Deletes);
        Assert.Equal(RequirementProcessingJobState.Running, (await CombinedJob()).State);
        await using var db = fixture.CreateDbContext(); Assert.Single(await db.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/1.pdf"), Token));
    }

    private sealed class StopWithReadClient(AssignedExecutionControl control) : IVerifiedExecutionDocumentClient
    {
        internal Stream? Reader; internal Task<int>? Read; internal int Calls;
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        {
            Calls++; Reader = request.Files[0].Content;
            // Deliberately non-cooperative transport read: owner must drain, not close its handle.
            Read = Reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();
            control.Stop(); return Task.FromResult(ConsumingClient.Response(request));
        }
    }
}
