using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Projects.GetProjectWorkspace;
using Application.PreQuotes.ProcessRequirement;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class WorkspaceSnapshotGate : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = Signal();
        private int held;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("users") && Interlocked.Exchange(ref held, 1) == 0)
            {
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class WorkspaceWriterPid : DbCommandInterceptor
    {
        internal int Pid;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Pid = ((NpgsqlConnection)command.Connection!).ProcessID; return ValueTask.FromResult(result); }
    }

    private CoordinateAssignedExecutionService WorkspaceCoordinator(Files files, Copies copies, ConsumingClient client, WorkspaceWriterPid probe)
    {
        var documents = Store(files, copies);
        return new(new(new AssignedJobStartStore(H.Options())), documents, Consumer(files, copies, client),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()), documents),
            new(new JobPublicationStore(H.Options(probe))), H.Service());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WorkspaceExtraction_Composition_ConcurrentWriter_ReturnsCoherentVersion(bool replacement)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var entered = Signal(); var releaseProvider = Signal();
        var copies = new Copies(); var client = new ConsumingClient();
        var writerProbe = new WorkspaceWriterPid();
        client.Action = async (request, ct) => { entered.TrySetResult(); await releaseProvider.Task.WaitAsync(ct); return ConsumingClient.Response(request); };
        Task<AssignedSupervisionResult>? execution = null;
        // Keep the first reader connection open so writers cannot reuse its physical PID.
        await using var read = fixture.CreateDbContext(); await read.Database.OpenConnectionAsync(Token);
        var readPid = ((NpgsqlConnection)read.Database.GetDbConnection()).ProcessID;
        var project = await read.Projects.SingleAsync(Token);
        var reached = Signal(); var releaseRead = Signal();
        var requirements = Substitute.For<IRequirementRepository>();
        requirements.GetCurrentByPreQuoteIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var value = await new RequirementRepository(read).GetCurrentByPreQuoteIdAsync(call.ArgAt<Guid>(0), Token);
            reached.TrySetResult(); await releaseRead.Task.WaitAsync(Token); return value;
        });
        var currentUser = Substitute.For<ICurrentUser>(); currentUser.IsAuthenticated.Returns(true); currentUser.UserId.Returns(a.ActorUserId);
        var publicContext = Substitute.For<IPublicExperienceProjectContextRepository>();
        var probe = new CombinedCommands();
        var service = new GetProjectWorkspaceService(new GetProjectWorkspaceQueryValidator(), currentUser,
            new IdentityRepository(read), new ProjectRepository(read), new ClientRepository(read), new PreQuoteRepository(read),
            requirements, publicContext, new WorkspaceExtractionReader(H.Options(probe)));
        Task<GetProjectWorkspaceResult>? pending = null;
        try
        {
            if (replacement)
                await H.Service().RequestJobCancellationAsync(new(a.JobId, a.ExecutionRequestId, a.RequirementId), a.ActorUserId, Token);
            else
            {
                var calls = SupervisionProbe();
                execution = new SuperviseAssignedExecutionService(new(calls), WorkspaceCoordinator(files, copies, client, writerProbe),
                    new(calls), new(calls), new ManualSupervisionClock()).ExecuteAsync(permit, Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            }
            pending = service.ExecuteAsync(new(project.Id), Token);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Guid expectedRequirement = a.RequirementId;
            if (replacement)
            {
                await using var writer = fixture.CreateDbContext(); await writer.Database.OpenConnectionAsync(Token);
                var writerPid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
                Assert.NotEqual(readPid, writerPid);
                using var content = new MemoryStream("new"u8.ToArray());
                var changed = await ExpiredReplacement(writer, a.ActorUserId, files).ReplaceRequirementAsync(
                    new(a.RequirementId, [new("next.pdf", "application/pdf", 3, content)]), Token);
                Assert.True(changed.IsSuccess, changed.Failure.ToString());
                expectedRequirement = (await new RequirementRepository(writer).GetCurrentByPreQuoteIdAsync(
                    (await writer.Requirements.SingleAsync(x => x.Id == a.RequirementId, Token)).PreQuoteId, Token))!.RequirementId;
                output.WriteLine($"Workspace replacement readPID={readPid}, writerPID={writerPid}; real writer committed while initial read return was held.");
            }
            else
            {
                releaseProvider.TrySetResult(); await execution!;
                Assert.NotEqual(readPid, writerProbe.Pid); Assert.NotEqual(0, writerProbe.Pid);
                output.WriteLine($"Workspace publication readPID={readPid}, writerPID={writerProbe.Pid}; real supervisor/publication committed while initial read return was held.");
            }
            var before = await CombinedSnapshot();
            releaseRead.TrySetResult(); var result = await pending;
            Assert.True(result.IsSuccess, result.Failure.ToString()); var workflow = result.Workspace!.Workflow;
            Assert.Equal(expectedRequirement, workflow.RequirementId);
            Assert.Equal(replacement ? "PENDING" : "PROCESSED", workflow.RequirementStatus);
            Assert.Equal(replacement ? "Manual" : "Succeeded", workflow.Extraction!.State);
            Assert.Equal(!replacement, workflow.HasTechnicalProposal);
            if (replacement) Assert.Null(workflow.TechnicalProposalId);
            else Assert.Equal((await read.RequirementTechnicalProposals.AsNoTracking().SingleAsync(Token)).Id, workflow.TechnicalProposalId);
            Assert.Equal(0, probe.Writes); Assert.Equal(before, await CombinedSnapshot());
        }
        finally
        {
            releaseProvider.TrySetResult(); releaseRead.TrySetResult();
            if (execution is not null) await execution;
            if (pending is not null) await pending;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WorkspaceExtraction_SnapshotBeforeWriter_RemainsCoherent(bool replacement)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var provider = Signal(); var releaseProvider = Signal(); var gate = new WorkspaceSnapshotGate();
        var copies = new Copies(); var client = new ConsumingClient(); var writerProbe = new WorkspaceWriterPid();
        client.Action = async (request, ct) => { provider.TrySetResult(); await releaseProvider.Task.WaitAsync(ct); return ConsumingClient.Response(request); };
        Task<AssignedSupervisionResult>? execution = null; Task<ProjectWorkspaceWorkflowResult?>? pending = null;
        await using var db = fixture.CreateDbContext(); var project = await db.Projects.SingleAsync(Token);
        var quote = (await db.Requirements.SingleAsync(x => x.Id == a.RequirementId, Token)).PreQuoteId;
        try
        {
            if (replacement) await H.Service().RequestJobCancellationAsync(new(a.JobId, a.ExecutionRequestId, a.RequirementId), a.ActorUserId, Token);
            else
            {
                var calls = SupervisionProbe();
                execution = new SuperviseAssignedExecutionService(new(calls), WorkspaceCoordinator(files, copies, client, writerProbe),
                    new(calls), new(calls), new ManualSupervisionClock()).ExecuteAsync(permit, Token);
                await provider.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            }
            var writes = new CombinedCommands();
            pending = new WorkspaceExtractionReader(H.Options(gate, writes)).ReadCurrentAsync(project.Id, quote, a.ActorUserId, Token);
            var readerPid = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            if (replacement)
            {
                await using var writer = fixture.CreateDbContext(); await writer.Database.OpenConnectionAsync(Token);
                writerProbe.Pid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
                using var content = new MemoryStream("new"u8.ToArray());
                var result = await ExpiredReplacement(writer, a.ActorUserId, files).ReplaceRequirementAsync(
                    new(a.RequirementId, [new("next.pdf", "application/pdf", 3, content)]), Token);
                Assert.True(result.IsSuccess, result.Failure.ToString());
            }
            else { releaseProvider.TrySetResult(); await execution!; }
            Assert.NotEqual(readerPid, writerProbe.Pid); Assert.NotEqual(0, writerProbe.Pid);
            var before = await CombinedSnapshot(); gate.Release.TrySetResult(); var snapshot = await pending;
            Assert.Equal(a.RequirementId, snapshot!.RequirementId);
            Assert.Equal(replacement ? "PENDING" : "PROCESSING", snapshot.RequirementStatus);
            Assert.Equal(replacement ? "Cancelled" : "Running", snapshot.Extraction!.State);
            Assert.False(snapshot.HasTechnicalProposal); Assert.Null(snapshot.TechnicalProposalId);
            Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
            var latest = await new WorkspaceExtractionReader(H.Options()).ReadCurrentAsync(project.Id, quote, a.ActorUserId, Token);
            Assert.Equal(replacement ? "Manual" : "Succeeded", latest!.Extraction!.State);
            output.WriteLine($"Workspace snapshot-before-writer replacement={replacement} readerPID={readerPid},writerPID={writerProbe.Pid}; SELECT executed before writer commit, response released afterwards; DML0.");
        }
        finally
        {
            releaseProvider.TrySetResult(); gate.Release.TrySetResult();
            if (execution is not null) await execution;
            if (pending is not null) await pending;
        }
    }
}
