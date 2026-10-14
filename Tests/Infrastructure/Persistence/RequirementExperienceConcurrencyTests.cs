using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.RequirementExperience;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[CollectionDefinition("Experience concurrency", DisableParallelization = true)]
public sealed class ExperienceConcurrencyCollection : ICollectionFixture<ExperienceConcurrencyFixture>;

public sealed class ExperienceConcurrencyFixture : IAsyncLifetime
{
    public PostgreSqlIntegrationFixture Database { get; } = new() { UseMigrations = false };
    public ValueTask InitializeAsync() => Database.InitializeAsync();
    public ValueTask DisposeAsync() => Database.DisposeAsync();
}

[Collection("Experience concurrency")]
[Trait("Category", "PostgreSql")]
public sealed class RequirementExperienceConcurrencyTests(ExperienceConcurrencyFixture fixture)
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Level2_TwoIndependentRequests_OnlyOneWins(bool existing)
    {
        var data = await SeedAsync();
        if (existing)
        {
            await using var setup = fixture.Database.CreateDbContext();
            setup.Add(RequirementItemExperienceTechnicalSelection.Create(data.Seed.TechnicalProposalId,
                data.Seed.FirstProposalItemId, null, null, data.User.Id, At));
            await setup.SaveChangesAsync();
        }

        var barrier = new SaveBarrier();
        await using var a = CreateContext("selection-a", barrier);
        await using var b = CreateContext("selection-b", barrier);
        long? revision = existing ? 1 : null;
        var first = SelectionService(a, data).UpdateSystemAsync(data.Seed.TechnicalProposalId,
            data.Seed.FirstProposalItemId, new(data.SystemId, revision), CancellationToken.None);
        var second = SelectionService(b, data).UpdateGlassAsync(data.Seed.TechnicalProposalId,
            data.Seed.FirstProposalItemId, new(data.GlassId, revision), CancellationToken.None);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.Error == RequirementExperienceFailure.Conflict);

        await using var verify = fixture.Database.CreateDbContext();
        var stored = Assert.Single(await verify.RequirementItemExperienceTechnicalSelections.ToListAsync());
        Assert.Equal(existing ? 2 : 1, stored.Revision);
        var systemWon = results[0].IsSuccess;
        Assert.Equal(systemWon ? data.SystemId : (Guid?)null, stored.SystemId);
        Assert.Equal(systemWon ? null : (Guid?)data.GlassId, stored.GlassTypeId);
        var item = await verify.RequirementTechnicalProposalItems.SingleAsync(x => x.Id == data.Seed.FirstProposalItemId);
        Assert.Null(item.SelectedSystemId);
        Assert.Null(item.SelectedGlassTypeId);
        Assert.Null(item.SelectedFinishTypeId);
        Assert.Null(item.SelectedAtUtc);
        var proposal = await verify.RequirementTechnicalProposals.SingleAsync(x => x.Id == data.Seed.TechnicalProposalId);
        Assert.Equal(1, proposal.CommercialRevision);
        Assert.Null(proposal.CommercialConfirmedAtUtc);
    }

    [Fact]
    public async Task Confirm_TwoRequests_CreateOneRevisionAndSecondReturnsLatest()
    {
        var data = await SeedAsync();
        await using var a = CreateContext("confirm-a");
        await using var b = CreateContext("confirm-b");
        var paused = new CurrentResolver(data, pause: true);
        var first = ConfirmationService(a, data, paused).ConfirmAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = ConfirmationService(b, data, new CurrentResolver(data))
            .ConfirmAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        try { await WaitForLockAsync("confirm-b"); }
        finally { paused.Release.TrySetResult(); }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value!.LatestConfirmationRevision);
        });
        Assert.Equal(1, paused.Calls);
        await using var verify = fixture.Database.CreateDbContext();
        Assert.Single(await verify.RequirementExperienceConfigurationConfirmations.ToListAsync());
        Assert.Equal(1, (await verify.RequirementTechnicalProposals.SingleAsync()).CommercialRevision);
    }

    [Fact]
    public async Task Confirm_PhysicalChangeAfterEvaluation_WaitsAndCannotChangeSnapshot()
    {
        var data = await SeedAsync();
        await using var a = CreateContext("snapshot");
        await using var b = CreateContext("physical-writer");
        var paused = new CurrentResolver(data, pause: true);
        var confirm = ConfirmationService(a, data, paused)
            .ConfirmAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var change = b.RequirementTechnicalProposalItems.Where(x => x.Id == data.Seed.FirstProposalItemId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BaseQuantity, (int?)null));
        try { await WaitForLockAsync("physical-writer"); }
        finally { paused.Release.TrySetResult(); }
        Assert.True((await confirm.WaitAsync(TimeSpan.FromSeconds(20))).IsSuccess);
        await change.WaitAsync(TimeSpan.FromSeconds(20));

        await using var verify = CreateContext("verify");
        var service = ConfirmationService(verify, data, new CurrentResolver(data));
        var status = await service.GetStatusAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        Assert.Equal(RequirementExperienceConfigurationStatuses.Stale, status.Value!.Status);
        Assert.False(status.Value.CanConfirm);
        var blocked = await service.ConfirmAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        Assert.Equal(RequirementExperienceFailure.ConfigurationBlocked, blocked.Error);
        var stored = Assert.Single(await verify.RequirementExperienceConfigurationConfirmations.Include(x => x.Items).ToListAsync());
        Assert.Equal(1, stored.Revision);
        Assert.Equal(1, Assert.Single(stored.Items).Quantity);
    }

    [Fact]
    public async Task Confirm_ChangeBeforeLocks_IsValidatedAndBlocked()
    {
        var data = await SeedAsync();
        await using var writer = CreateContext("before-writer");
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.RequirementTechnicalProposalItems.Where(x => x.Id == data.Seed.FirstProposalItemId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BaseQuantity, (int?)null));
        await using var reader = CreateContext("before-confirm");
        var confirm = ConfirmationService(reader, data, new CurrentResolver(data))
            .ConfirmAsync(data.Seed.TechnicalProposalId, CancellationToken.None);
        try { await WaitForLockAsync("before-confirm"); }
        finally { await transaction.CommitAsync(); }
        var result = await confirm.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(RequirementExperienceFailure.ConfigurationBlocked, result.Error);
        Assert.Empty(await reader.RequirementExperienceConfigurationConfirmations.ToListAsync());
    }

    [Fact]
    public async Task ConfirmationUniqueRace_IsConflictNotPersistenceFailure()
    {
        var data = await SeedAsync();
        await using var a = CreateContext("unique-a");
        await using var b = CreateContext("unique-b");
        var ra = new RequirementRepository(a);
        var rb = new RequirementRepository(b);
        ra.AddExperienceConfigurationConfirmation(RequirementExperienceConfigurationConfirmation.Create(
            data.Seed.RequirementId, data.Seed.TechnicalProposalId, 1, data.User.Id, At));
        rb.AddExperienceConfigurationConfirmation(RequirementExperienceConfigurationConfirmation.Create(
            data.Seed.RequirementId, data.Seed.TechnicalProposalId, 1, data.User.Id, At));
        await ra.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<RequirementExperienceConflictException>(() => rb.SaveChangesAsync(CancellationToken.None));
        await using var verify = CreateContext("unique-verify");
        Assert.Single(await verify.RequirementExperienceConfigurationConfirmations.ToListAsync());
    }

    [Fact]
    public async Task UnrelatedForeignKeyFailure_RemainsPersistenceError()
    {
        var data = await SeedAsync();
        await using var db = CreateContext("fk");
        var repository = new RequirementRepository(db);
        repository.AddExperienceTechnicalSelection(RequirementItemExperienceTechnicalSelection.Create(
            data.Seed.TechnicalProposalId, data.Seed.FirstProposalItemId,
            Guid.NewGuid(), null, data.User.Id, At));
        await Assert.ThrowsAsync<RequirementPersistenceException>(() => repository.SaveChangesAsync(CancellationToken.None));
    }

    private ApplicationDbContext CreateContext(string applicationName, SaveChangesInterceptor? interceptor = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.Database.ConnectionString!)
        {
            ApplicationName = applicationName
        };
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ApplicationDbContext(builder.Options);
    }

    private async Task WaitForLockAsync(string applicationName)
    {
        await using var connection = new NpgsqlConnection(fixture.Database.ConnectionString!);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND application_name = @name AND wait_event_type = 'Lock')",
                connection);
            command.Parameters.AddWithValue("name", applicationName);
            if (await command.ExecuteScalarAsync() is true) return;
            await Task.Delay(50);
        }
        Assert.Fail("Expected the independent request to wait on a PostgreSQL row lock.");
    }

    private static (ICurrentUser Current, IIdentityRepository Identity) Auth(User user)
    {
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(user.Id);
        var identity = Substitute.For<IIdentityRepository>();
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return (current, identity);
    }

    private static UpdateRequirementExperienceLevel2SelectionService SelectionService(ApplicationDbContext db, Data data)
    {
        var auth = Auth(data.User);
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { System("FIXED", "FIXED") with { Id = data.SystemId } });
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Glass("TEMP_5", "MONOLITHIC", "TEMPERED") with { GlassTypeId = data.GlassId } });
        return new(new RequirementRepository(db), auth.Identity, auth.Current, systems, glasses,
            new RequirementExperienceLevel2Resolver(new SgProductSystemConstraintEvaluator(TimeProvider.System),
                new GlassCandidateResolver(), new DeterministicSgTechnicalSelector(systems)), TimeProvider.System);
    }

    private static RequirementExperienceConfigurationService ConfirmationService(
        ApplicationDbContext db, Data data, CurrentResolver resolver)
    {
        var auth = Auth(data.User);
        return new(new RequirementRepository(db), auth.Identity, auth.Current, resolver, TimeProvider.System);
    }

    // Supply a resolved finish only in this test: production still correctly blocks pending finishes.
    private sealed class CurrentResolver(Data data, bool pause = false) : IRequirementExperienceConfigurationResolver
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public async Task<RequirementExperienceCurrentConfiguration> ResolveAsync(
            RequirementTechnicalProposal proposal, CancellationToken cancellationToken)
        {
            Calls++;
            var item = proposal.Items.Single(x => x.Id == data.Seed.FirstProposalItemId);
            var value = RequirementExperienceCurrentConfigurationItem.Included(item.Id, item.Reference, item.Sequence,
                item.EffectiveQuantity, item.EffectiveWidthMillimeters, item.EffectiveHeightMillimeters,
                1m, item.EffectiveLocation, data.SystemId, data.GlassId, data.FinishId,
                "RESOLVED", "RESOLVED", "RESOLVED", "NONE", "NONE", [], []);
            var current = new RequirementExperienceCurrentConfiguration(proposal.RequirementId, proposal.Id, [value]);
            Entered.TrySetResult();
            if (pause) await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return current;
        }
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    private async Task<Data> SeedAsync()
    {
        var seed = await SeedExperienceTechnicalProposalAsync();
        await using var db = fixture.Database.CreateDbContext();
        var user = await db.Users.SingleAsync(x => x.Id == seed.UserId);
        var system = global::Domain.Catalogs.ProductSystem.Create("FIXED", "Fixed", true, true, false, false, At,
            functionalType: "FIXED", isSelectable: true);
        var glass = GlassType.Create("TEMP_5", "Tempered", null, At, family: "MONOLITHIC", composition: "TEMPERED");
        var finish = FinishType.Create("FINISH", "Finish", false, At);
        db.AddRange(system, glass, finish);
        db.Add(RequirementItemExperienceDraft.Create(seed.TechnicalProposalId, seed.FirstProposalItemId,
            "sng-experience-v4-001", null, [
                new("THERMAL", "THERMAL_LOW"), new("ACOUSTIC", "ACOUSTIC_LOW"),
                new("SECURITY", "SECURITY_LOW"), new("UV", "UV_NO"), new("AESTHETICS", "AESTHETICS_LOW")
            ], user.Id, At));
        await db.SaveChangesAsync();
        return new(seed, user, system.Id, glass.Id, finish.Id);
    }

    private sealed record Data(SeededExperienceTechnicalProposal Seed, User User, Guid SystemId, Guid GlassId, Guid FinishId);
    private sealed record SeededPreQuote(Guid UserId, Guid PreQuoteId);
    private sealed record SeededExperienceTechnicalProposal(
        Guid UserId, Guid TechnicalProposalId, Guid FirstProposalItemId, Guid SecondProposalItemId, Guid RequirementId);
    private static RequirementExtractedItem CreateExtractedItem(
        Guid extractionId,
        int sequence,
        string reference,
        string functionalType = "FIXED",
        string? operation = null) =>
        RequirementExtractedItem.Create(
            extractionId,
            $"ai2-{sequence}",
            sequence,
            reference,
            $"Item {sequence}",
            StructuredElementType.Window,
            1,
            1000,
            1000,
            1m,
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            functionalType,
            operation,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            At.AddMinutes(5));

    private static RequirementTechnicalProposalItem CreateProposalItem(
        Guid proposalId,
        Guid extractedItemId,
        Guid? suggestedSystemId = null,
        Guid? suggestedGlassTypeId = null,
        Guid? suggestedFinishTypeId = null,
        int sequence = 1) =>
        RequirementTechnicalProposalItem.Create(
            proposalId,
            extractedItemId,
            suggestedSystemId,
            suggestedGlassTypeId,
            suggestedFinishTypeId,
            sequence,
            $"V-{sequence}",
            "Ventana",
            StructuredElementType.Window,
            1,
            1000,
            1000,
            0.9m,
            0.9m,
            0.9m,
            0.9m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "NO_HISTORY",
            At.AddMinutes(6));

    private static ProductSystemCatalogReadModel System(
        string code,
        string? functionalType,
        bool requiresReview = false,
        string? commercialLine = null) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            code,
            code,
            functionalType,
            "FAMILY",
            "SERIES",
            commercialLine,
            "STANDARD",
            IsSelectable: true,
            ActiveForRecognition: true,
            Priceable: true,
            FuturePriceable: false,
            requiresReview,
            IsActive: true,
            Constraints: []);

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string family,
        string composition,
        bool requiresReview = false) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            null,
            IsActive: true,
            CurrentPriceRange: null,
            Family: family,
            Composition: composition,
            Treatment: null,
            OuterThicknessMm: 5m,
            InnerThicknessMm: null,
            PvbThicknessMm: null,
            PvbType: null,
            PvbColor: null,
            ChamberThicknessMm: null,
            ProductLine: null,
            ProductToken: null,
            Pattern: null,
            Color: null,
            IsSelectable: true,
            RequiresReview: requiresReview);

    private async Task<SeededExperienceTechnicalProposal>
        SeedExperienceTechnicalProposalAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedPreQuoteAsync();
        var requirement = Requirement.Create(
            seeded.PreQuoteId,
            seeded.UserId,
            RequirementCommercialLine.Essential,
            At.AddMinutes(1));
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            seeded.UserId,
            Guid.NewGuid(),
            At.AddMinutes(2));
        attempt.Start(At.AddMinutes(3));
        attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(4));
        var extraction = RequirementExtractionResult.Create(
            attempt.Id,
            "3.0",
            "AI2",
            "{}",
            2,
            0,
            0,
            0,
            "test",
            100,
            At.AddMinutes(5));
        var firstExtracted = CreateExtractedItem(extraction.Id, 1, "V-01");
        var secondExtracted = CreateExtractedItem(extraction.Id, 2, "V-02");
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            attempt.Id,
            false,
            At.AddMinutes(6));
        var firstProposalItem = CreateProposalItem(proposal.Id, firstExtracted.Id);
        var secondProposalItem = CreateProposalItem(proposal.Id, secondExtracted.Id, sequence: 2);
        proposal.AddItem(firstProposalItem);
        proposal.AddItem(secondProposalItem);

        await using var context = fixture.Database.CreateDbContext();
        context.Requirements.Add(requirement);
        context.RequirementProcessingAttempts.Add(attempt);
        context.RequirementExtractionResults.Add(extraction);
        context.RequirementExtractedItems.AddRange(firstExtracted, secondExtracted);
        context.RequirementTechnicalProposals.Add(proposal);
        await context.SaveChangesAsync(cancellationToken);

        return new SeededExperienceTechnicalProposal(
            seeded.UserId,
            proposal.Id,
            firstProposalItem.Id,
            secondProposalItem.Id,
            requirement.Id);
    }

    private async Task<SeededPreQuote> SeedPreQuoteAsync()
    {
        fixture.Database.RequireAvailable();
        await fixture.Database.ResetAsync();
        await using var context = fixture.Database.CreateDbContext();
        var user = User.CreateFromGoogle(
            "owner@example.com",
            "Owner",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Client",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            user.Id,
            At);
        var project = Project.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            "Bogota",
            user.Id,
            At);
        var preQuote = PreQuote.Create(project.Id, user.Id, "PC-2020-0001", null, At);
        context.Users.Add(user);
        context.Clients.Add(client);
        context.Projects.Add(project);
        context.PreQuotes.Add(preQuote);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededPreQuote(user.Id, preQuote.Id);
    }

}
