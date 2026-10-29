using System.Data.Common;
using System.Text.Json;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class PublicSubmissionEvidencePostgreSqlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueIdRace_ObservedPostgresLock_PreservesWinningEvidence(bool different)
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-public-evidence-").FullName;
        var before = Spools();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = new CommitBarrier(release.Task);
        var loser = new InsertProbe();
        Task<global::Application.PublicExperience.PublicExperienceIntakeResult>? a = null, b = null;
        await using var first = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(winner).Options);
        await using var second = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(loser).Options);
        try
        {
            var storage = Storage(root);
            var one = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            var two = PublicExperienceIntakeTests.Upload(different ? "xyz"u8.ToArray() : "abc"u8.ToArray());
            a = Service(first, storage).ExecuteAsync(PublicExperienceIntakeTests.Payload([one]), [one], Token);
            var winnerPid = await winner.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            b = Service(second, storage).ExecuteAsync(PublicExperienceIntakeTests.Payload([two]), [two], Token);
            var loserPid = await loser.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(winnerPid, loserPid);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loserPid, winnerPid);
            output.WriteLine($"different={different}; winner={winnerPid}; loser={loserPid}; Lock and named blocker observed");
            release.TrySetResult();
            Assert.Equal(201, (await a).StatusCode);
            Assert.Equal(different ? 409 : 200, (await b).StatusCode);
            await using var read = fixture.CreateDbContext();
            var row = await read.Set<PublicExperienceSubmission>().Include(s => s.Files).SingleAsync(Token);
            var file = Assert.Single(row.Files);
            Assert.Equal(Hash, file.ContentHash); Assert.Equal(Fingerprint, row.PayloadFingerprint);
            Assert.Equal(3, file.StoredLengthBytes); Assert.Equal((short)1, file.StoredEvidenceVersion);
            Assert.NotNull(file.StoredEvidenceAtUtc);
            Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(root, file.StorageKey), Token));
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            var snapshot = JsonSerializer.Serialize(row);
            await using var replay = fixture.CreateDbContext();
            Assert.True((await Service(replay, storage).ExecuteAsync(PublicExperienceIntakeTests.Payload([one]), [one], Token)).IsReplay);
            await using var verify = fixture.CreateDbContext();
            Assert.Equal(snapshot, JsonSerializer.Serialize(await verify.Set<PublicExperienceSubmission>().Include(s => s.Files).SingleAsync(Token)));
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.Equal(before, Spools());
        }
        finally
        {
            release.TrySetResult();
            try { await Task.WhenAll(new[] { a, b }.Where(t => t is not null).Select(t => t!)); }
            finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
        }
    }

    private sealed class CommitBarrier(Task release) : DbTransactionInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await release.WaitAsync(cancellationToken);
            return result;
        }
    }
    private sealed class InsertProbe : DbCommandInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO core.public_experience_submissions", StringComparison.Ordinal))
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
