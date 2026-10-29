using System.Diagnostics;
using Application.PreQuotes.ProcessRequirement;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobProtocolReconciliationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData("start")] [InlineData("request")] [InlineData("confirm")] [InlineData("fail")] [InlineData("publish")]
    public async Task RealObservationBudget_CancelsServerBlockedReconciliation_WithoutWrites(string route)
    {
        var s = new JobProtocolTestSupport(fixture);
        var seed = await s.H.Create();
        if (route != "start") seed = await s.Start(seed);
        if (route == "confirm") await s.H.Call("request", seed);
        var prepared = route == "publish" ? await s.Prepare(seed) : null;
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(Token);
        NpgsqlTransaction? blockingTransaction = null;
        var locked = Signal();
        var loseResponse = Signal();
        var observing = false;
        var probe = new JobProtocolTestSupport.QuoteProbe(() => observing);
        var writes = new JobProtocolTestSupport.WriteProbe(() => observing);
        var afterCommit = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            // This callback runs after an actual successful commit, never instead of one.
            blockingTransaction = await blocker.BeginTransactionAsync(Token);
            await using var command = blocker.CreateCommand();
            command.Transaction = blockingTransaction;
            command.CommandText = "SELECT id FROM core.pre_quotes WHERE id = @id FOR UPDATE";
            command.Parameters.AddWithValue("id", seed.Quote);
            await command.ExecuteScalarAsync(Token);
            locked.TrySetResult();
            await loseResponse.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            observing = true;
            throw new IOException("Injected response loss after real commit; observation must contend with external lock");
        });
        async Task<object> Run()
        {
            if (route == "start") return await s.H.StartService(afterCommit, probe, writes).StartAsync(seed.Execution, seed.Actor, Token);
            if (route == "publish") return await s.P.Service(afterCommit, probe, writes).PublishAsync(seed.Execution, seed.Actor, prepared!, Token);
            return await s.H.Call(route, seed, s.H.Service(afterCommit, probe, writes));
        }
        var pending = Run();
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            var committed = await s.P.Snapshot();
            var elapsed = Stopwatch.StartNew();
            loseResponse.TrySetResult();
            var observerPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(8), Token);
            Assert.NotEqual(blocker.ProcessID, observerPid);
            await s.WaitBlocked(observerPid, blocker.ProcessID);
            // Safety timeout belongs to the test. The production observation token remains ten seconds.
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(45), Token);
            Assert.True(probe.Cancelled.Task.IsCompletedSuccessfully);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9), $"Budget ended unexpectedly early: {elapsed.Elapsed}");
            if (route == "start") Assert.Equal(AssignedJobStartState.Indeterminate, Assert.IsType<AssignedJobStartResult>(result).State);
            else if (route == "publish") Assert.Equal(JobPublicationState.Indeterminate, Assert.IsType<JobPublicationResult>(result).State);
            else Assert.Equal(JobTerminationState.Indeterminate, Assert.IsType<JobTerminationResult>(result).State);
            Assert.Equal(committed, await s.P.Snapshot());
            Assert.Equal(0, writes.Count);
            output.WriteLine($"{route}: observer PID={observerPid}, blocker PID={blocker.ProcessID}; server Lock observed; "
                + $"production token cancelled; Indeterminate after {elapsed.Elapsed.TotalSeconds:F2}s including disposal; no reconciliation writes");
        }
        finally
        {
            loseResponse.TrySetResult();
            if (blockingTransaction is not null)
            {
                await blockingTransaction.RollbackAsync(CancellationToken.None);
                await blockingTransaction.DisposeAsync();
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(45), CancellationToken.None);
        }
    }
}
