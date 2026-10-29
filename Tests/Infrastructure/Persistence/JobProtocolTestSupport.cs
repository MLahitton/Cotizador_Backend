using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

internal sealed class JobProtocolTestSupport(PostgreSqlIntegrationFixture fixture)
{
    internal JobTerminationTestSupport H => new(fixture);
    internal JobPublicationTestSupport P => new(fixture);

    internal async Task<PreparedJobPublication> Prepare(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        var requirement = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == s.Execution.RequirementId, Token);
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Execution.JobId, Token);
        var files = await db.RequirementFiles.AsNoTracking().Where(x => x.RequirementId == requirement.Id)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(Token);
        var request = new DocumentProcessingClientRequest(files[0].Id, s.Execution.ProcessingAttemptId!.Value, job.CorrelationId,
            files.Select(f => new DocumentProcessingFile(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, Stream.Null)).ToArray());
        var raw = JsonSerializer.Serialize(new { requirement = new { }, evidence = Array.Empty<object>(), extraction_metadata = new { },
            elements = new[] { new { name = "Window", quantity = 1, width_mm = 1000, height_mm = 2000 } } });
        var response = new Ai2RequirementExtractionAdapter().Adapt(raw, request);
        var repository = Substitute.For<IRequirementRepository>();
        var result = await new PrepareJobPublicationService(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(repository))
            .PrepareAsync(new(requirement.Id, request.ProcessingAttemptId, requirement.CommercialLine!.Value, files[0].Id,
                files.Select(f => new PublicationFile(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.CreatedAtUtc)).ToArray()),
                response, Token);
        Assert.Empty(repository.ReceivedCalls());
        return result;
    }

    internal async Task<Seed> Start(Seed s)
    {
        var result = await H.StartService().StartAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedJobStartState.Started, result.State);
        return s with { Execution = s.Execution with { ProcessingAttemptId = result.Receipt!.ProcessingAttemptId } };
    }

    internal async Task WaitBlocked(int pid, int? blocker = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(deadline.Token);
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0 "
                + "AND (@blocker = 0 OR @blocker = ANY(pg_blocking_pids(pid))) FROM pg_stat_activity WHERE pid = @pid";
            command.Parameters.AddWithValue("pid", pid);
            command.Parameters.AddWithValue("blocker", blocker ?? 0);
            if (await command.ExecuteScalarAsync(deadline.Token) is true) return;
            // Poll server lock state, not elapsed sleep, to prove overlap before releasing the winner.
            await Task.Delay(20, deadline.Token);
        }
    }

    internal sealed class QuoteProbe(Func<bool>? enabled = null) : DbCommandInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = Signal();
        internal Stopwatch Elapsed { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if ((enabled?.Invoke() ?? true) && command.CommandText.Contains("pre_quotes") && command.CommandText.Contains("FOR UPDATE"))
            {
                if (!Elapsed.IsRunning) Elapsed.Start();
                cancellationToken.Register(() => Cancelled.TrySetResult());
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class AfterCommit(Func<Task> callback) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => callback();
    }

    // Covers the INSERT/UPDATE/DELETE commands emitted by these EF stores, including rolled-back writes.
    internal sealed class WriteProbe(Func<bool>? enabled = null) : DbCommandInterceptor
    {
        private int count;
        internal int Count => Volatile.Read(ref count);
        private void Observe(DbCommand command)
        {
            var sql = command.CommandText.TrimStart();
            if ((enabled?.Invoke() ?? true) && new[] { "INSERT ", "UPDATE ", "DELETE " }
                .Any(prefix => sql.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                Interlocked.Increment(ref count);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
    }
}
