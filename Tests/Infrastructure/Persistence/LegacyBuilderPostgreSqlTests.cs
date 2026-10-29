using Application.Common.Abstractions.Catalogs;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class LegacyBuilderPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobTerminationTestSupport H => new(fixture);
    private Task<string> Snapshot() => new JobPublicationTestSupport(fixture).Snapshot();
    private sealed record Seed(Guid Requirement, Guid Actor, Guid Attempt, Guid Extraction);
    private static readonly Type[] SourceTypes = [typeof(RequirementExtractionResult), typeof(RequirementExtractedItem),
        typeof(RequirementExtractedItemEvidence), typeof(RequirementExtractedItemSegment)];

    private async Task<string> SourceSnapshot()
    {
        await using var db = fixture.CreateDbContext();
        var rows = new SortedDictionary<string, string[]>();
        foreach (var type in SourceTypes)
        {
            var entity = db.Model.FindEntityType(type)!;
            var table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
#pragma warning disable EF1002 // Fixed source types, SQL identifiers exclusively from EF metadata.
            var values = await db.Database.SqlQueryRaw<string>(
                $"SELECT row_to_json(t)::text AS \"Value\" FROM {table} t ORDER BY id").ToArrayAsync(Token);
#pragma warning restore EF1002
            Assert.NotEmpty(values);
            rows.Add(table, values);
        }
        Assert.Empty(db.ChangeTracker.Entries());
        return JsonSerializer.Serialize(rows);
    }

    private sealed class BuilderSqlProbe : DbCommandInterceptor
    {
        internal List<string> Writes { get; } = [];
        private void Capture(DbCommand command)
        {
            foreach (Match match in Regex.Matches(command.CommandText,
                @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+core\.\w+", RegexOptions.IgnoreCase))
                Writes.Add(match.Value);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Capture(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Capture(command); return ValueTask.FromResult(result); }
    }

    private async Task<Seed> Create()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Builder test", null, null, null, null, null, null, null, user.Id, at);
        var project = Project.Create(client.Id, "BUILD", "Builder test", null, null, user.Id, at);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, at);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, at);
        db.AddRange(user, client, project, quote, requirement);
        var (attempt, extraction) = AddExtraction(db, requirement.Id, user.Id, at.AddMinutes(1));
        await db.SaveChangesAsync(Token);
        return new(requirement.Id, user.Id, attempt.Id, extraction.Id);
    }

    private static (RequirementProcessingAttempt, RequirementExtractionResult) AddExtraction(
        ApplicationDbContext db, Guid requirement, Guid actor, DateTimeOffset at)
    {
        var attempt = RequirementProcessingAttempt.Create(requirement, actor, Guid.NewGuid(), at);
        attempt.Start(at);
        attempt.Complete(DocumentProcessingOutcome.RequiresReview, at);
        var input = PreparedJobPublicationTests.Input().Extraction;
        var extraction = RequirementExtractionResult.Create(attempt.Id, "3.0", "AI2", input.Payload.GetRawText(), 1, 1, 0, 0, "test", 1, at);
        var item = JobPublicationGraph.Materialize(input.Items[0], extraction.Id, at);
        db.AddRange(attempt, extraction, item);
        return (attempt, extraction);
    }

    private async Task AddJob(Seed s, string state = "Queued")
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var job = RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, now);
        if (state != "Queued") job.AssignExecution("test", now, now.AddMinutes(10));
        if (state == "Succeeded") { job.LinkStartedAttempt(s.Attempt); job.Succeed(now); }
        if (state == "Failed") job.Fail("TEST_FAILURE", now);
        if (state == "Cancelled") { job.RequestCancellation(now); job.ConfirmCancellation(now); }
        db.Add(job);
        await db.SaveChangesAsync(Token);
    }

    [Theory]
    [InlineData("Pending")] [InlineData("Processed")] [InlineData("Failed")]
    public async Task AuthorizedOwner_PersistsGraphWithoutNewLifecycleRestriction(string status)
    {
        var s = await Create();
        await using (var setup = fixture.CreateDbContext())
        {
            var requirement = await setup.Requirements.SingleAsync(Token);
            if (status != "Pending")
            {
                requirement.StartProcessing(DateTimeOffset.UtcNow);
                if (status == "Processed") requirement.MarkProcessed(DateTimeOffset.UtcNow);
                else requirement.MarkFailed(DateTimeOffset.UtcNow);
                await setup.SaveChangesAsync(Token);
            }
        }
        var operational = await H.Snapshot();
        var sources = await SourceSnapshot();
        await using (var before = fixture.CreateDbContext())
            Assert.Empty(await before.RequirementTechnicalProposals.AsNoTracking().ToListAsync(Token));
        var sql = new BuilderSqlProbe();
        await using var db = new ApplicationDbContext(H.Options(sql));
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.Null(db.Database.CurrentTransaction);
            return Task.FromResult<IReadOnlyList<ProductSystemCatalogReadModel>>([]);
        });
        var result = await PrepareJobPublicationTests.Builder(new RequirementRepository(db), systems).ExecuteAsync(s.Requirement, s.Actor, Token);
        Assert.True(result.IsSuccess);
        await using var verify = fixture.CreateDbContext();
        var proposal = await verify.RequirementTechnicalProposals.AsNoTracking().SingleAsync(Token);
        Assert.Equal(s.Requirement, proposal.RequirementId);
        Assert.Equal(s.Extraction, proposal.RequirementExtractionResultId);
        Assert.Equal(s.Attempt, proposal.RequirementProcessingAttemptId);
        var proposalItem = Assert.Single(await verify.RequirementTechnicalProposalItems.AsNoTracking().ToListAsync(Token));
        var sourceItem = await verify.RequirementExtractedItems.AsNoTracking().SingleAsync(Token);
        Assert.Equal(sourceItem.Id, proposalItem.RequirementExtractedItemId);
        Assert.Equal(proposal.Id, proposalItem.TechnicalProposalId);
        Assert.Equal(s.Extraction, sourceItem.RequirementExtractionResultId);
        Assert.Equal(sources, await SourceSnapshot());
        Assert.Equal(operational, await H.Snapshot());
        Assert.Equal(new[] { "INSERT INTO core.requirement_technical_proposals", "INSERT INTO core.requirement_technical_proposal_items" }, sql.Writes);
        Assert.All(db.ChangeTracker.Entries().Where(e => SourceTypes.Contains(e.Metadata.ClrType)),
            e => Assert.Equal(EntityState.Unchanged, e.State));
        output.WriteLine($"{status}: all source rows/columns unchanged in independent scalar snapshots; SQL: {string.Join("; ", sql.Writes)}");
    }

    [Theory]
    [InlineData("empty", ProcessRequirementFailure.InvalidRequest)]
    [InlineData("inactive", ProcessRequirementFailure.InactiveUser)]
    [InlineData("foreign", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData("admin", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData("client", ProcessRequirementFailure.InactiveClient)]
    [InlineData("project", ProcessRequirementFailure.InactiveProject)]
    public async Task Authorization_PrecedesHistory_AndNeverWrites(string kind, ProcessRequirementFailure expected)
    {
        var s = await Create();
        await AddJob(s);
        var actor = s.Actor;
        await using (var setup = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            if (kind == "empty") actor = Guid.Empty;
            if (kind == "inactive") (await setup.Users.SingleAsync(Token)).Deactivate(now);
            if (kind is "foreign" or "admin")
            {
                var other = User.CreateFromGoogle("foreign@test.local", "Foreign", null, null, now);
                if (kind == "admin") other.ChangeRole(UserRole.Admin, now);
                setup.Add(other);
                actor = other.Id;
            }
            if (kind == "client") (await setup.Set<Client>().SingleAsync(Token)).SetActive(false, actor, now);
            if (kind == "project") (await setup.Set<Project>().SingleAsync(Token)).SetActive(false, actor, now);
            await setup.SaveChangesAsync(Token);
        }
        var before = await Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(writes));
        var builder = PrepareJobPublicationTests.Builder(new RequirementRepository(db));
        Assert.Equal(expected, (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(
            () => builder.ExecuteAsync(s.Requirement, actor, Token))).Failure);
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData("Queued")] [InlineData("Running")] [InlineData("Succeeded")] [InlineData("Failed")] [InlineData("Cancelled")]
    public async Task AnyHistory_RejectsPersistentEntry_ButBuildRemainsPure(string state)
    {
        var s = await Create();
        await AddJob(s, state);
        var before = await Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(writes));
        var repository = new RequirementRepository(db);
        var builder = PrepareJobPublicationTests.Builder(repository);
        Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob,
            (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(() => builder.ExecuteAsync(s.Requirement, s.Actor, Token))).Failure);
        var extraction = await repository.GetLatestSuccessfulExtractionAsync(s.Requirement, Token);
        var items = await repository.GetExtractedItemsAsync(s.Extraction, Token);
        var prepared = await builder.BuildAsync(s.Requirement, RequirementCommercialLine.Essential, extraction!, items, Token);
        Assert.Single(prepared.Items);
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData("job", ProcessRequirementFailure.ProcessingManagedByJob)]
    [InlineData("inactive", ProcessRequirementFailure.InactiveUser)]
    [InlineData("project", ProcessRequirementFailure.InactiveProject)]
    [InlineData("extraction", ProcessRequirementFailure.PersistenceError)]
    [InlineData("items", ProcessRequirementFailure.PersistenceError)]
    public async Task FinalGuard_RevalidatesAfterPreparation(string change, ProcessRequirementFailure expected)
    {
        var s = await Create();
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(writes));
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        string? changedSnapshot = null;
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Assert.Null(db.Database.CurrentTransaction);
            if (changedSnapshot is not null) return (IReadOnlyList<ProductSystemCatalogReadModel>)Array.Empty<ProductSystemCatalogReadModel>();
            if (change == "job") await AddJob(s);
            else
            {
                await using var other = fixture.CreateDbContext();
                var now = DateTimeOffset.UtcNow;
                if (change == "inactive") (await other.Users.SingleAsync(Token)).Deactivate(now);
                if (change == "project") (await other.Set<Project>().SingleAsync(Token)).SetActive(false, s.Actor, now);
                if (change == "extraction") AddExtraction(other, s.Requirement, s.Actor, now);
                if (change == "items") other.Add(JobPublicationGraph.Materialize(
                    PreparedJobPublicationTests.Input().Extraction.Items[0] with { Sequence = 2 }, s.Extraction, now));
                await other.SaveChangesAsync(Token);
            }
            changedSnapshot = await Snapshot();
            return (IReadOnlyList<ProductSystemCatalogReadModel>)Array.Empty<ProductSystemCatalogReadModel>();
        });
        var builder = PrepareJobPublicationTests.Builder(new RequirementRepository(db), systems);
        Assert.Equal(expected, (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(
            () => builder.ExecuteAsync(s.Requirement, s.Actor, Token))).Failure);
        Assert.NotNull(changedSnapshot);
        Assert.Equal(changedSnapshot, await Snapshot());
        Assert.Equal(0, writes.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CommitFailure_DoesNotRetryOrCompensate(bool committed)
    {
        var s = await Create();
        var commits = 0;
        var hook = new CommitHook(() => { commits++; throw new IOException("injected commit failure"); }, committed);
        var operational = await H.Snapshot();
        await using var db = new ApplicationDbContext(H.Options(hook));
        var builder = PrepareJobPublicationTests.Builder(new RequirementRepository(db));
        Assert.Equal(ProcessRequirementFailure.PersistenceError,
            (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(() => builder.ExecuteAsync(s.Requirement, s.Actor, Token))).Failure);
        Assert.Equal(1, commits);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(committed ? 1 : 0, await verify.RequirementTechnicalProposals.CountAsync(Token));
        Assert.Equal(committed ? 1 : 0, await verify.RequirementTechnicalProposalItems.CountAsync(Token));
        Assert.Equal(operational, await H.Snapshot());
    }

    [Fact]
    public async Task SaveFailure_RollsBackEntireProposalGraph()
    {
        var s = await Create();
        var before = await Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var injected = false;
        await using var db = new ApplicationDbContext(H.Options(writes,
            new Saved(_ => { injected = true; throw new DbUpdateException("injected graph save failure"); })));
        await Assert.ThrowsAsync<RequirementPersistenceException>(() =>
            PrepareJobPublicationTests.Builder(new RequirementRepository(db)).ExecuteAsync(s.Requirement, s.Actor, Token));
        Assert.True(writes.Count > 0);
        Assert.True(injected);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task ExistingCommercialProposal_IsNotRegenerated()
    {
        var s = await Create();
        await using (var setup = fixture.CreateDbContext())
        {
            await PrepareJobPublicationTests.Builder(new RequirementRepository(setup)).ExecuteAsync(s.Requirement, s.Actor, Token);
        }
        await using (var edit = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var system = ProductSystem.Create("TEST_SYSTEM", "Test system", true, true, false, false, now);
            var glass = GlassType.Create("TEST_GLASS", "Test glass", null, now);
            var finish = FinishType.Create("TEST_FINISH", "Test finish", false, now);
            edit.AddRange(system, glass, finish);
            var proposal = await edit.RequirementTechnicalProposals.Include(x => x.Items).SingleAsync(Token);
            var item = Assert.Single(proposal.Items);
            item.UpdateManualLocation("Commercial location");
            item.Select(system.Id, glass.Id, finish.Id, s.Actor, now);
            proposal.MarkCommerciallyChanged();
            // Domain-valid confirmation fixture; service readiness is covered by the existing protocol suite.
            proposal.ConfirmCommercialSelection(s.Actor, now);
            Assert.NotNull(proposal.CommercialConfirmedAtUtc);
            await edit.SaveChangesAsync(Token);
        }
        var before = await Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(writes));
        Assert.Equal(ProcessRequirementFailure.PersistenceError, (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(() =>
            PrepareJobPublicationTests.Builder(new RequirementRepository(db)).ExecuteAsync(s.Requirement, s.Actor, Token))).Failure);
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task JobWriterWinsDuringPreparation_FinalTransactionWaitsAndRejects()
    {
        var s = await Create();
        var preparing = Signal();
        var resume = Signal();
        var finalPhase = false;
        var probe = new JobProtocolTestSupport.QuoteProbe(() => finalPhase);
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(probe, writes));
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Assert.Null(db.Database.CurrentTransaction);
            preparing.TrySetResult();
            await resume.Task.WaitAsync(Token);
            finalPhase = true;
            return (IReadOnlyList<ProductSystemCatalogReadModel>)Array.Empty<ProductSystemCatalogReadModel>();
        });
        var pending = Assert.ThrowsAsync<LegacyProcessingRejectedException>(() =>
            PrepareJobPublicationTests.Builder(new RequirementRepository(db), systems).ExecuteAsync(s.Requirement, s.Actor, Token));
        await using var other = fixture.CreateDbContext();
        IRequirementLegacyWrite? transaction = null;
        try
        {
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            transaction = await new RequirementRepository(other).BeginLegacyWriteAsync(s.Requirement, s.Actor, true, Token);
            Assert.All(transaction.Attempts, a => Assert.Equal(DocumentProcessingState.Finished, a.ProcessingState));
            var now = DateTimeOffset.UtcNow;
            other.Add(RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(),
                RequirementProcessingJobTrigger.ManualInitial, now));
            await other.SaveChangesAsync(Token);
            var winner = ((NpgsqlConnection)other.Database.GetDbConnection()).ProcessID;
            resume.TrySetResult();
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"job winner PID={winner}; builder final PID={loser}; PostgreSQL Lock and pg_blocking_pids observed.");
            await transaction.CommitAsync(Token);
            await transaction.DisposeAsync();
            transaction = null;
            var committed = await Snapshot();
            Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob, (await pending).Failure);
            Assert.Equal(committed, await Snapshot());
            Assert.Equal(0, writes.Count);
            await using var verify = fixture.CreateDbContext();
            Assert.Single(await verify.RequirementProcessingJobs.ToListAsync(Token));
            Assert.Empty(await verify.RequirementTechnicalProposals.ToListAsync(Token));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            resume.TrySetResult();
            await pending;
        }
    }

    [Fact]
    public async Task ConcurrentWriters_SerializeAndCannotReplaceWinner()
    {
        var s = await Create();
        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var probe = new JobProtocolTestSupport.QuoteProbe();
        await using var firstDb = new ApplicationDbContext(H.Options(new Saved(async db =>
        {
            held.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            await release.Task.WaitAsync(Token);
        })));
        await using var secondDb = new ApplicationDbContext(H.Options(probe));
        var first = PrepareJobPublicationTests.Builder(new RequirementRepository(firstDb)).ExecuteAsync(s.Requirement, s.Actor, Token);
        Task<LegacyProcessingRejectedException>? second = null;
        try
        {
            var winner = await held.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var pendingRejection = Assert.ThrowsAsync<LegacyProcessingRejectedException>(() =>
                PrepareJobPublicationTests.Builder(new RequirementRepository(secondDb)).ExecuteAsync(s.Requirement, s.Actor, Token));
            second = pendingRejection;
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"builder winner PID={winner}; loser PID={loser}; PostgreSQL Lock and pg_blocking_pids observed.");
            release.TrySetResult();
            Assert.True((await first).IsSuccess);
            Assert.Equal(ProcessRequirementFailure.PersistenceError, (await pendingRejection).Failure);
            await using var verify = fixture.CreateDbContext();
            Assert.Single(await verify.RequirementTechnicalProposals.ToListAsync(Token));
            Assert.Single(await verify.RequirementTechnicalProposalItems.ToListAsync(Token));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(new Task?[] { first, second }.OfType<Task>());
        }
    }
}
