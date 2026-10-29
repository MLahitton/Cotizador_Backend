using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Fact]
    public async Task ClosureA_Drain_RealOutstandingRead_NoProofOrDisposalUntilJoined()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var copies = new BlockingReadCopies(); var client = new ClosureReadingClient(caller);
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);
        var coordinator = new CoordinateAssignedExecutionService(new(new AssignedJobStartStore(H.Options())), store,
            new(store, client, NullLogger<ConsumeExecutionDocumentsService>.Instance),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>())),
            new(new JobPublicationStore(H.Options())), H.Service());
        var calls = SupervisionProbe(); var invocation = new LocalExecutionInvocation(calls, coordinator, new(calls), new(calls), TimeProvider.System);
        var input = SupervisionRequest(seed); var running = invocation.ExecuteAsync(input, seed.Actor, caller.Token);
        var failure = Assert.ThrowsAsync<OperationCanceledException>(() => running);
        LiveCancellation? close = null;
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (client.Reader is null || client.Reader.CanRead) { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
            Assert.False(running.IsCompleted); Assert.Null(invocation.DrainCompletion); Assert.Equal(0, copies.Disposals);
            var job = await CombinedJob();
            var execution = new AssignedJobExecution(job.Id, job.ExecutionRequestId, job.RequirementId, job.LeaseOwner!, job.LeaseGeneration, job.ProcessingAttemptId);
            Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", seed with { Execution = execution })).State);
            job = await CombinedJob(); close = new(new(job.Id, job.RequirementId, job.ExecutionRequestId, input.OperationId),
                job.LeaseOwner!, job.LeaseGeneration, job.StartedAtUtc!.Value, job.ProcessingAttemptId, job.CancellationRequestedAtUtc!.Value);
            var opened = 0; var before = await CombinedSnapshot();
            var result = await Closer(write: [new OpenHook(() => opened++)]).ConfirmStoppedCancellationAsync(close, invocation.DrainCompletion, seed.Actor, Token);
            Assert.Equal(JobTerminationFailure.ContextConflict, result.Failure); Assert.Equal(0, opened); Assert.Null(result.Receipt);
            Assert.Equal(before, await CombinedSnapshot()); Assert.Equal(0, copies.Disposals);
        }
        finally { copies.Release.TrySetResult(); await failure; if (client.Read is not null) await client.Read; }
        Assert.Equal(caller.Token, (await failure).CancellationToken); Assert.Equal(2, copies.Disposals); Assert.Equal(1, client.Calls);
        var proof = Assert.IsType<LocalDrainCompletion>(invocation.DrainCompletion); Assert.Empty(typeof(LocalDrainCompletion).GetConstructors());
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await Closer().ConfirmStoppedCancellationAsync(close!, proof, seed.Actor, Token)).State);
        Assert.Equal(0, files.Deletes);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/1.pdf"), Token));
    }

    private sealed class ClosureReadingClient(CancellationTokenSource caller) : IVerifiedExecutionDocumentClient
    {
        internal Stream? Reader; internal Task<int>? Read; internal int Calls;
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        {
            Calls++; Reader = request.Files[0].Content;
            Read = Reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();
            caller.Cancel(); return Task.FromResult(ConsumingClient.Response(request));
        }
    }

    [Fact]
    public async Task ClosureA_Drain_ForeignInvocationAndRecognizedClaim_DoNotAuthorizeClose()
    {
        await using var files1 = new Files(); var first = await ClosureSetup(files1, false);
        // Each setup resets this isolated fixture. Keep the genuine old proof, never forge a Task/DTO.
        await using var files2 = new Files(); var second = await ClosureSetup(files2, false);
        var opened = 0; var before = await CombinedSnapshot();
        var rejected = await Closer(write: [new OpenHook(() => opened++)])
            .ConfirmStoppedCancellationAsync(second.Input, first.Drain, second.Seed.Actor, Token);
        Assert.Equal(JobTerminationFailure.ContextConflict, rejected.Failure); Assert.Null(rejected.Receipt);
        Assert.Equal(0, opened); Assert.Equal(before, await CombinedSnapshot());
        var calls = SupervisionProbe(); var copies = new Copies(); var client = new ConsumingClient();
        var replay = new LocalExecutionInvocation(calls, Coordinator(files2, copies, client), new(calls), new(calls), TimeProvider.System);
        var id = second.Input.Identity;
        var result = await replay.ExecuteAsync(new(id.JobId, id.RequirementId, id.ExecutionRequestId, id.ClaimOperationId,
            Guid.Parse(second.Input.LeaseOwner.Split(':')[1])), second.Seed.Actor, Token);
        Assert.Equal(AssignedSupervisionOutcome.NotStarted, result.Outcome); Assert.Null(replay.DrainCompletion);
        Assert.Equal(0, client.Calls); Assert.Empty(copies.Created); Assert.Equal(before, await CombinedSnapshot());
    }

    [Fact]
    public async Task ClosureA_PublishedAndCancelledSuccessor_HistoryPreserved()
    {
        await using var files = new Files(); var seed = await SeedFiles(files, start: false, assigned: false);
        var calls = SupervisionProbe(); var copies = new Copies(); var client = new ConsumingClient();
        var invocation = new LocalExecutionInvocation(calls, Coordinator(files, copies, client), new(calls), new(calls), TimeProvider.System);
        var request = SupervisionRequest(seed); var result = await invocation.ExecuteAsync(request, seed.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Published, result.Coordinator!.Outcome);
        var proof = Assert.IsType<LocalDrainCompletion>(invocation.DrainCompletion); var job = await CombinedJob();
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var input = new LiveCancellation(proof.Identity, proof.LeaseOwner, proof.LeaseGeneration, proof.AssignedAtUtc, job.ProcessingAttemptId, await H.Now());
        var rejected = await Closer([writes], [writes]).ConfirmStoppedCancellationAsync(input, proof, seed.Actor, Token);
        Assert.Equal(JobTerminationFailure.TerminalConflict, rejected.Failure); Assert.Null(rejected.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot()); Released(files, copies, client);
    }

    [Fact]
    public async Task ClosureA_CancelledSuccessor_ReplayWithoutCurrentOrDrain()
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        var closed = await Closer().ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.CancellationConfirmed, closed.State);
        await using (var db = fixture.CreateDbContext())
        {
            var old = await db.Requirements.SingleAsync(Token);
            var next = Requirement.Create(old.PreQuoteId, s.Seed.Actor, RequirementCommercialLine.Signature, await H.Now());
            old.SupersedeAfterConfirmedCancellationBy(next.Id, await H.Now()); db.Add(next); await db.SaveChangesAsync(Token);
        }
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var result = await Closer([writes], [writes]).ConfirmStoppedCancellationAsync(s.Input, null, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.AlreadyRecognized, result.State); Assert.Equal(closed.Receipt, result.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
    }
}
