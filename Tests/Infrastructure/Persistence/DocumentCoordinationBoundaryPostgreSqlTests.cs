using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;
using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    [Fact]
    public async Task DocumentFromAnotherRequirement_CannotBeRemovedOrReplaced()
    {
        var seed = await ManualSeed(2);
        Guid foreignFile;
        await using (var db = fixture.CreateDbContext())
        {
            var quote = await db.PreQuotes.SingleAsync(Token);
            var at = DateTimeOffset.UtcNow;
            var otherQuote = PreQuote.Create(quote.ProjectId, seed.Actor, "PC-2026-0002", null, at);
            var other = Requirement.Create(otherQuote.Id, seed.Actor, RequirementCommercialLine.Essential, at);
            var file = RequirementFile.Create(other.Id, "other.pdf", "application/pdf", 3, "tests/other.pdf", at);
            foreignFile = file.Id;
            db.AddRange(otherQuote, other, file);
            await db.SaveChangesAsync(Token);
        }
        using var storage = await Files(seed);
        using var foreignBytes = new MemoryStream([1, 2, 3]);
        await storage.Inner.SaveAsync("tests/other.pdf", foreignBytes, Token);
        var files = await FileSnapshot();
        var operational = await Protocol.P.Snapshot();
        await using var context = fixture.CreateDbContext();
        var service = Service(context, seed.Actor, storage);
        using var content = new MemoryStream([8, 9]);
        Assert.Equal(ManageRequirementDocumentsFailure.DocumentNotFound,
            (await service.ReplaceDocumentAsync(new(seed.Execution.RequirementId, foreignFile,
                new("new.pdf", "application/pdf", 2, content)), Token)).Failure);
        Assert.Equal(ManageRequirementDocumentsFailure.DocumentNotFound,
            (await service.RemoveDocumentAsync(new(seed.Execution.RequirementId, foreignFile), Token)).Failure);
        Assert.Equal(files, await FileSnapshot());
        Assert.Equal(operational, await Protocol.P.Snapshot());
        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.Bytes("tests/other.pdf"));
        Assert.Equal(0, storage.Writes);
        Assert.Equal(0, storage.Deletes);
    }

    [Fact]
    public async Task CommercialConfirmation_RemainsAvailable_AndReplacementPreservesIt()
    {
        var published = await Protocol.P.Create(review: false);
        Assert.Equal(JobPublicationState.Published, (await Protocol.P.Publish(published)).State);
        var seed = published.Execution;
        using var storage = await Files(seed);
        await using (var db = fixture.CreateDbContext())
        {
            var proposal = await db.RequirementTechnicalProposals.Include(p => p.Items).SingleAsync(Token);
            var item = Assert.Single(proposal.Items);
            item.UpdateManualLocation("Preserved commercial location");
            proposal.MarkCommerciallyChanged();
            await db.SaveChangesAsync(Token);
            var confirmation = await new ConfirmRequirementTechnicalProposalSelectionService(Actor(seed.Actor),
                new IdentityRepository(db), new RequirementRepository(db), new PreQuoteRepository(db),
                new ProjectRepository(db), new ClientRepository(db), new ProductSystemCatalogRepository(db), TimeProvider.System)
                .ExecuteAsync(new(proposal.Id), Token);
            Assert.True(confirmation.IsSuccess, confirmation.Failure.ToString());
            Assert.Equal(2, proposal.CommercialRevision);
            Assert.NotNull(proposal.CommercialConfirmedAtUtc);
            Assert.True(item.HasSelectedConfiguration());
        }
        var graph = await HistoricalGraph();
        await using var write = fixture.CreateDbContext();
        using var content = new MemoryStream([8, 9]);
        Assert.True((await Service(write, seed.Actor, storage).ReplaceRequirementAsync(
            new(seed.Execution.RequirementId, [new("next.pdf", "application/pdf", 2, content)]), Token)).IsSuccess);
        Assert.Equal(graph, await HistoricalGraph());
        var stable = await Protocol.P.Snapshot();
        await Protocol.P.Publish(published);
        Assert.Equal(stable, await Protocol.P.Snapshot());
    }

    [Theory]
    [InlineData("pending", true, false)]
    [InlineData("processed", false, true)]
    [InlineData("failed", false, true)]
    [InlineData("cancelled", false, false)]
    public async Task NoJobs_PreservesLocalEligibility(string state, bool editable, bool replaceable)
    {
        var seed = await ManualSeed();
        await using (var db = fixture.CreateDbContext())
        {
            var requirement = await db.Requirements.SingleAsync(Token);
            var now = DateTimeOffset.UtcNow;
            if (state == "cancelled") requirement.Cancel(now);
            if (state is "processed" or "failed")
            {
                requirement.StartProcessing(now);
                if (state == "processed") requirement.MarkProcessed(now); else requirement.MarkFailed(now);
            }
            await db.SaveChangesAsync(Token);
        }
        await using var read = fixture.CreateDbContext();
        Assert.Equal(new RequirementDocumentCapabilities(editable, editable, replaceable),
            await new RequirementRepository(read).GetDocumentCapabilitiesAsync(seed.Execution.RequirementId, seed.Actor, Token));
    }

    [Theory]
    [InlineData("foreign", ManageRequirementDocumentsFailure.RequirementNotFound)]
    [InlineData("admin", ManageRequirementDocumentsFailure.RequirementNotFound)]
    [InlineData("user", ManageRequirementDocumentsFailure.InactiveUser)]
    [InlineData("project", ManageRequirementDocumentsFailure.InactiveProject)]
    [InlineData("client", ManageRequirementDocumentsFailure.InactiveClient)]
    public async Task Authorization_IsRecheckedBeforeHistory(string state, ManageRequirementDocumentsFailure expected)
    {
        var seed = await SeedState("queued");
        var actor = seed.Actor;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            if (state is "foreign" or "admin")
            {
                var foreign = User.CreateFromGoogle("foreign@doc.test", "Other", null, null, now);
                if (state == "admin") foreign.ChangeRole(UserRole.Admin, now);
                actor = foreign.Id;
                db.Add(foreign);
            }
            if (state == "user") (await db.Users.SingleAsync(Token)).Deactivate(now);
            if (state == "project") (await db.Projects.SingleAsync(Token)).SetActive(false, actor, now);
            if (state == "client") (await db.Set<Client>().SingleAsync(Token)).SetActive(false, actor, now);
            await db.SaveChangesAsync(Token);
        }
        using var storage = await Files(seed);
        await using var context = fixture.CreateDbContext();
        var repo = new RequirementRepository(context);
        Assert.Equal(RequirementDocumentCapabilities.None,
            await repo.GetDocumentCapabilitiesAsync(seed.Execution.RequirementId, actor, Token));
        var rejected = await Assert.ThrowsAsync<RequirementDocumentRejectedException>(() =>
            repo.BeginDocumentWriteAsync(seed.Execution.RequirementId, actor, Token));
        Assert.Equal(expected, rejected.Failure);
        Assert.Equal(expected, (await Service(context, actor, storage).CancelAsync(new(seed.Execution.RequirementId), Token)).Failure);
        Assert.Equal(0, storage.Writes);
        Assert.Equal(0, storage.Deletes);
    }

    [Fact]
    public async Task ConfirmationMustCommitBeforeReplacement_LateExecutionCannotChangeSuccessor()
    {
        var seed = await Protocol.Start(await Protocol.H.Create(publicContext: true));
        var prepared = await Protocol.Prepare(seed);
        Assert.Equal(JobTerminationState.CancellationRequested, (await Protocol.H.Call("request", seed)).State);
        using var storage = await Files(seed);
        var reached = Signal();
        var release = Signal();
        var hook = new CommandHook(afterJobWrite: async _ =>
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(Token);
        });
        var confirmation = Protocol.H.Call("confirm", seed, Protocol.H.Service(hook));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await using var before = fixture.CreateDbContext();
            using var bytes = new MemoryStream([8, 9]);
            Assert.Equal(ManageRequirementDocumentsFailure.RequirementNotReplaceable,
                (await Service(before, seed.Actor, storage).ReplaceRequirementAsync(
                    new(seed.Execution.RequirementId, [new("new.pdf", "application/pdf", 2, bytes)]), Token)).Failure);
            Assert.Equal(0, storage.Writes);
            release.TrySetResult();
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await confirmation).State);
            var history = await HistoricalGraph();
            await using var db = fixture.CreateDbContext();
            var sourceBefore = await db.Set<PublicExperienceSubmission>().AsNoTracking()
                .Select(s => new { s.Id, s.RequirementId, s.PreQuoteId, s.AnswersJson, s.PayloadFingerprint, s.UpdatedAtUtc }).ToArrayAsync(Token);
            Assert.True((await Service(db, seed.Actor, storage).ReplaceRequirementAsync(
                new(seed.Execution.RequirementId, [new("new.pdf", "application/pdf", 2, bytes)]), Token)).IsSuccess);
            Assert.Equal(history, await HistoricalGraph());
            var stable = await Protocol.P.Snapshot();
            await Protocol.H.StartService().StartAsync(seed.Execution, seed.Actor, Token);
            var published = await Protocol.P.Service().PublishAsync(seed.Execution, seed.Actor, prepared, Token);
            Assert.NotEqual(JobPublicationState.Published, published.State);
            Assert.Equal(stable, await Protocol.P.Snapshot());
            await using var read = fixture.CreateDbContext();
            Assert.Equal(sourceBefore, await read.Set<PublicExperienceSubmission>().AsNoTracking()
                .Select(s => new { s.Id, s.RequirementId, s.PreQuoteId, s.AnswersJson, s.PayloadFingerprint, s.UpdatedAtUtc }).ToArrayAsync(Token));
            var successor = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id != seed.Execution.RequirementId, Token);
            Assert.True(successor.IsCurrent);
            Assert.Equal(RequirementStatus.Pending, successor.Status);
        }
        finally { release.TrySetResult(); await confirmation; }
    }
}
