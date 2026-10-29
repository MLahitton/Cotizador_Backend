using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobProtocolRacePostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobProtocolTestSupport S => new(fixture);

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StartVersusCancellation_BothDecisiveLockOrders(bool cancellationFirst)
    {
        var seed = await S.H.Create();
        DateTimeOffset originalActivity;
        await using (var initial = fixture.CreateDbContext())
            originalActivity = (await initial.PreQuotes.SingleAsync(Token)).UpdatedAtUtc;
        var guarded = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var hold = new CommandHook(async command =>
        {
            guarded.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        });
        var probe = new JobProtocolTestSupport.QuoteProbe();
        Task<AssignedJobStartResult>? start = null;
        Task<JobTerminationResult>? cancel = null;
        try
        {
            if (cancellationFirst) cancel = S.H.Call("request", seed, S.H.Service(hold));
            else start = S.H.StartService(hold).StartAsync(seed.Execution, seed.Actor, Token);
            var firstPid = await guarded.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            if (cancellationFirst) start = S.H.StartService(probe).StartAsync(seed.Execution, seed.Actor, Token);
            else cancel = S.H.Call("request", seed, S.H.Service(probe));
            var secondPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(firstPid, secondPid);
            await S.WaitBlocked(secondPid, firstPid);
            output.WriteLine($"cancelFirst={cancellationFirst}: PID {secondPid} blocked by {firstPid} on PostgreSQL lock");
        }
        finally
        {
            release.TrySetResult();
            var pending = new List<Task>();
            if (start is not null) pending.Add(start);
            if (cancel is not null) pending.Add(cancel);
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(45), CancellationToken.None);
        }
        var started = await start!;
        Assert.Equal(JobTerminationState.CancellationRequested, (await cancel!).State);
        if (cancellationFirst) Assert.Equal(AssignedJobStartFailure.CancellationRequested, started.Failure);
        else
        {
            Assert.Equal(AssignedJobStartState.Started, started.State);
            seed = seed with { Execution = seed.Execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId } };
        }
        await using var read = fixture.CreateDbContext();
        var before = await read.PreQuotes.AsNoTracking().SingleAsync(Token);
        Assert.Equal(cancellationFirst ? 0 : 1, await read.RequirementProcessingAttempts.CountAsync(Token));
        var confirmed = await S.H.Call("confirm", seed);
        Assert.Equal(JobTerminationState.CancellationConfirmed, confirmed.State);
        var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Cancelled, job.State);
        Assert.Equal(cancellationFirst ? 0 : 1, job.AttemptCount);
        Assert.Equal(seed.Execution.ProcessingAttemptId, job.ProcessingAttemptId);
        Assert.Equal(RequirementStatus.Pending, (await read.Requirements.AsNoTracking().SingleAsync(Token)).Status);
        var activity = (await read.PreQuotes.AsNoTracking().SingleAsync(Token)).UpdatedAtUtc;
        if (cancellationFirst)
        {
            Assert.Equal(originalActivity, before.UpdatedAtUtc);
            Assert.Equal(originalActivity, activity);
        }
        else
        {
            var attempt = await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
            Assert.Equal(seed.Execution.ProcessingAttemptId, attempt.Id);
            Assert.Equal(DocumentProcessingOutcome.Cancelled, attempt.Outcome);
            Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
            Assert.Equal(job.FinishedAtUtc, activity);
        }
        Assert.False(await read.RequirementExtractionResults.AnyAsync(Token));
        Assert.False(await read.RequirementTechnicalProposals.AnyAsync(Token));
        var terminal = await S.P.Snapshot();
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await S.H.Call("confirm", seed)).State);
        Assert.Equal(terminal, await S.P.Snapshot());
    }

    [Theory]
    [InlineData("publish")] [InlineData("request")] [InlineData("fail")]
    public async Task ThreeParticipants_ControlledFirstOperation_IsOnlyWinner(string winner)
    {
        var s = await S.P.Create();
        var guarded = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var hold = new CommandHook(async command =>
        {
            guarded.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        });
        async Task<object> Run(string operation, IInterceptor hook) => operation == "publish"
            ? await S.P.Publish(s, S.P.Service(hook))
            : await S.H.Call(operation, s.Execution, S.H.Service(hook));
        var tasks = new Dictionary<string, Task<object>> { [winner] = Run(winner, hold) };
        var pids = new List<int>();
        try
        {
            pids.Add(await guarded.Task.WaitAsync(TimeSpan.FromSeconds(20), Token));
            foreach (var other in new[] { "publish", "request", "fail" }.Where(op => op != winner))
            {
                var probe = new JobProtocolTestSupport.QuoteProbe();
                tasks.Add(other, Run(other, probe));
                var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                pids.Add(pid);
                await S.WaitBlocked(pid);
            }
            Assert.Equal(3, pids.Distinct().Count());
            Assert.All(pids, pid => Assert.True(pid > 0));
            output.WriteLine($"winner={winner}; three PIDs={string.Join(',', pids)}; both competitors observed waiting on server locks");
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(tasks.Values).WaitAsync(TimeSpan.FromSeconds(45), CancellationToken.None);
        }
        var publication = Assert.IsType<JobPublicationResult>(await tasks["publish"]);
        var cancellation = Assert.IsType<JobTerminationResult>(await tasks["request"]);
        var failure = Assert.IsType<JobTerminationResult>(await tasks["fail"]);
        Assert.Equal(winner == "publish" ? JobPublicationState.Published : JobPublicationState.Conflict, publication.State);
        Assert.Equal(winner == "request" ? JobTerminationState.CancellationRequested : JobTerminationState.Conflict, cancellation.State);
        Assert.Equal(winner == "fail" ? JobTerminationState.Failed : JobTerminationState.Conflict, failure.State);
        await using var read = fixture.CreateDbContext();
        var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        var attempt = await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(attempt.Id, job.ProcessingAttemptId);
        Assert.Equal(s.Execution.Execution.ProcessingAttemptId, attempt.Id);
        Assert.Equal(job.CorrelationId, attempt.CorrelationId);
        Assert.Equal(winner == "publish" ? 1 : 0, await read.RequirementExtractionResults.CountAsync(Token));
        Assert.Equal(winner == "publish" ? 1 : 0, await read.RequirementTechnicalProposals.CountAsync(Token));
        if (winner == "request")
        {
            Assert.Equal(RequirementProcessingJobState.Running, job.State);
            Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState);
            Assert.Null(job.FinishedAtUtc);
            Assert.Equal(cancellation.Receipt!.CancellationRequestedAtUtc, job.CancellationRequestedAtUtc);
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await S.H.Call("confirm", s.Execution)).State);
        }
        else
        {
            Assert.Equal(winner == "publish" ? RequirementProcessingJobState.Succeeded : RequirementProcessingJobState.Failed, job.State);
            Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
            Assert.Equal(winner == "publish" ? s.Snapshot.ReadInput().Outcome : DocumentProcessingOutcome.Failed, attempt.Outcome);
            Assert.Equal(winner == "publish" ? publication.Receipt!.FinishedAtUtc : failure.Receipt!.FinishedAtUtc, job.FinishedAtUtc);
            Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
            Assert.Equal(job.LastErrorCode, attempt.ErrorCode);
        }
        var terminal = await S.P.Snapshot();
        if (winner == "publish") Assert.Equal(publication.Receipt, (await S.P.Publish(s)).Receipt);
        else Assert.Equal(JobPublicationState.Conflict, (await S.P.Publish(s)).State);
        await S.H.Call("request", s.Execution);
        await S.H.Call("fail", s.Execution);
        Assert.Equal(terminal, await S.P.Snapshot());
        var requirement = await read.Requirements.AsNoTracking().SingleAsync(Token);
        Assert.Equal(winner == "publish" ? RequirementStatus.Processed : winner == "fail" ? RequirementStatus.Failed : RequirementStatus.Pending, requirement.Status);
        var finished = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(finished.FinishedAtUtc, (await read.PreQuotes.AsNoTracking().SingleAsync(Token)).UpdatedAtUtc);
        if (winner == "request")
        {
            Assert.Equal(RequirementProcessingJobState.Cancelled, finished.State);
            Assert.Equal(DocumentProcessingOutcome.Cancelled, (await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token)).Outcome);
        }
    }
}
