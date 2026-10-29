using System.Text.Json;
using System.Text.Json.Nodes;
using Application;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.PreQuotes;
using Infrastructure.Operations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    [Fact]
    public Task JobFirst_BlocksRequirementCancellation_WithoutLoserEffects() => CancellationJobRace(false);

    [Fact]
    public Task RequirementCancellationFirst_RejectsJobBecauseNotCurrent() => CancellationJobRace(true);

    private async Task CancellationJobRace(bool cancellationFirst)
    {
        var seed = await ManualSeed();
        using var storage = await Files(seed);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        string[] before;
        await using (var initial = fixture.CreateDbContext())
        {
            Assert.True((await new RequirementRepository(initial).GetDocumentCapabilitiesAsync(
                seed.Execution.RequirementId, seed.Actor, ct)).CanCancel);
            Assert.False(await initial.RequirementProcessingJobs.AnyAsync(ct));
            Assert.False(await initial.RequirementProcessingAttempts.AnyAsync(ct));
            before = await CancellationSnapshot(initial, ct);
        }
        var filesBefore = await FileSnapshot();
        var graphBefore = await HistoricalGraph();
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        string[]? winnerSnapshot = null;
        Guid? insertedJob = null;
        var winnerHook = new Saved(async db =>
        {
            winnerSnapshot = await CancellationSnapshot((ApplicationDbContext)db, ct);
            reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            await release.Task.WaitAsync(ct);
        });
        var probe = new JobProtocolTestSupport.QuoteProbe();
        var registry = new ObservedCancellationRegistry();
        var key = RequirementOperationKeys.Pricing(seed.Execution.RequirementId);
        var observedToken = registry.Register(key, CancellationToken.None);
        var signals = 0;
        using var callback = observedToken.Register(() => Interlocked.Increment(ref signals));
        var providerResolutions = 0;

        // Resolve the real application registration and its repository graph, not a service substitute.
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentUser>(Actor(seed.Actor));
        services.AddSingleton<IFileStorage>(storage);
        services.AddSingleton<IOperationCancellationRegistry>(registry);
        services.AddSingleton<IAi2DocumentProcessingClient>(_ =>
        {
            Interlocked.Increment(ref providerResolutions);
            throw new InvalidOperationException("Document cancellation must not resolve a provider.");
        });
        services.AddScoped(_ => new ApplicationDbContext(Protocol.H.Options(cancellationFirst ? winnerHook : probe)));
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IRequirementRepository, RequirementRepository>();
        services.AddScoped<IPreQuoteRepository, PreQuoteRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Same(registry, container.GetRequiredService<IOperationCancellationRegistry>());

        async Task<string> Cancel()
        {
            await using var scope = container.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ManageRequirementDocumentsService>()
                .CancelAsync(new(seed.Execution.RequirementId), ct);
            return result.IsSuccess ? "cancelled" : result.Failure.ToString();
        }

        async Task<string> InsertJob()
        {
            await using var db = new ApplicationDbContext(Protocol.H.Options(cancellationFirst ? probe : winnerHook));
            await using var write = await new RequirementRepository(db)
                .BeginDocumentWriteAsync(seed.Execution.RequirementId, seed.Actor, ct);
            if (!write.Requirement.IsCurrent) return "not_current";
            Assert.Equal(RequirementStatus.Pending, write.Requirement.Status);
            Assert.False(await db.RequirementProcessingAttempts.AnyAsync(a => a.RequirementId == write.Requirement.Id
                && (a.ProcessingState == DocumentProcessingState.Pending || a.ProcessingState == DocumentProcessingState.Processing), ct));
            Assert.False(await db.RequirementProcessingJobs.AnyAsync(j => j.RequirementId == write.Requirement.Id, ct));
            var job = RequirementProcessingJob.Create(write.Requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
                RequirementProcessingJobTrigger.ManualInitial, DateTimeOffset.UtcNow);
            insertedJob = job.Id;
            db.Add(job);
            await db.SaveChangesAsync(ct);
            await write.CommitAsync(ct);
            return "inserted";
        }

        Task<string>? winner = null;
        Task<string>? loser = null;
        try
        {
            winner = cancellationFirst ? Cancel() : InsertJob();
            var winnerPid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            loser = cancellationFirst ? InsertJob() : Cancel();
            var loserPid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            Assert.NotEqual(winnerPid, loserPid);
            await Protocol.WaitBlocked(loserPid, winnerPid);
            Assert.False(winner.IsCompleted);
            Assert.False(loser.IsCompleted);
            output.WriteLine($"cancel-first={cancellationFirst}: winner PID {winnerPid}; blocked PID {loserPid}; PostgreSQL Lock and named blocker observed");
            release.TrySetResult();
            Assert.Equal(cancellationFirst ? "cancelled" : "inserted", await winner);
            Assert.Equal(cancellationFirst ? "not_current" : nameof(ManageRequirementDocumentsFailure.RequirementNotMutable), await loser);

            await using var read = fixture.CreateDbContext();
            var after = await CancellationSnapshot(read, ct);
            Assert.NotNull(winnerSnapshot);
            Assert.Equal(winnerSnapshot, after);
            var requirement = await read.Requirements.AsNoTracking().SingleAsync(ct);
            var quote = await read.PreQuotes.AsNoTracking().SingleAsync(ct);
            Assert.Equal(seed.Execution.RequirementId, requirement.Id);
            Assert.Equal(cancellationFirst ? RequirementStatus.Cancelled : RequirementStatus.Pending, requirement.Status);
            Assert.Equal(!cancellationFirst, requirement.IsCurrent);
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(ct));
            if (cancellationFirst)
            {
                Assert.Null(insertedJob);
                Assert.False(await read.RequirementProcessingJobs.AnyAsync(ct));
                Assert.Equal(before[3], after[3]);
                AssertOnlyCancellationFieldsChanged(before[0], after[0], "status", "is_active", "updated_at_utc");
                AssertOnlyCancellationFieldsChanged(before[1], after[1], "updated_at_utc");
                Assert.True(requirement.UpdatedAtUtc > requirement.CreatedAtUtc);
                Assert.Equal(requirement.UpdatedAtUtc, quote.UpdatedAtUtc);
            }
            else
            {
                Assert.Equal(insertedJob, (await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(ct)).Id);
                Assert.Equal(before[0], after[0]);
                Assert.Equal(before[1], after[1]);
            }
            Assert.Equal(before[2], after[2]);
            Assert.Equal(before[4], after[4]);
            Assert.Equal(filesBefore, await FileSnapshot());
            if (cancellationFirst) Assert.Equal(graphBefore, await HistoricalGraph());
            Assert.Equal(new byte[10], await storage.Bytes("tests/no-read.pdf"));
            Assert.Equal(0, storage.Writes);
            Assert.Equal(0, storage.Deletes);
            Assert.Equal(0, registry.CancelCalls);
            Assert.Equal(1, registry.RegisterCalls);
            Assert.Equal(0, registry.CompleteCalls);
            Assert.False(observedToken.IsCancellationRequested);
            Assert.Equal(0, signals);
            Assert.Equal(0, providerResolutions);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                var pending = new[] { winner, loser }.OfType<Task<string>>().ToArray();
                await Task.WhenAll(pending);
            }
            finally { registry.Complete(key); }
        }
    }

    private static async Task<string[]> CancellationSnapshot(ApplicationDbContext db, CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var table in new[] { "requirements", "pre_quotes", "requirement_files", "requirement_processing_jobs", "requirement_processing_attempts" })
        {
#pragma warning disable EF1002 // Closed test-only table list, never request input.
            var rows = await db.Database.SqlQueryRaw<string>($"SELECT row_to_json(t)::text AS \"Value\" FROM core.{table} t ORDER BY id").ToArrayAsync(ct);
#pragma warning restore EF1002
            result.Add(JsonSerializer.Serialize(rows.Select(row => JsonNode.Parse(row)).ToArray()));
        }
        return result.ToArray();
    }

    private static void AssertOnlyCancellationFieldsChanged(string before, string after, params string[] allowed)
    {
        var left = JsonNode.Parse(before)!.AsArray();
        var right = JsonNode.Parse(after)!.AsArray();
        Assert.Single(left);
        Assert.Single(right);
        foreach (var field in allowed)
        {
            Assert.True(left[0]!.AsObject().Remove(field));
            Assert.True(right[0]!.AsObject().Remove(field));
        }
        Assert.True(JsonNode.DeepEquals(left, right));
    }

    private sealed class ObservedCancellationRegistry : IOperationCancellationRegistry
    {
        private readonly InMemoryOperationCancellationRegistry inner = new();
        internal int CancelCalls;
        internal int RegisterCalls;
        internal int CompleteCalls;
        public CancellationToken Register(string key, CancellationToken ct)
        { Interlocked.Increment(ref RegisterCalls); return inner.Register(key, ct); }
        public bool TryCancel(string key)
        { Interlocked.Increment(ref CancelCalls); return inner.TryCancel(key); }
        public void Complete(string key)
        { Interlocked.Increment(ref CompleteCalls); inner.Complete(key); }
    }
}
