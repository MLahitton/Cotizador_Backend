using Application.PreQuotes.RequirementExperience;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.PublicExperience;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class RequirementExperienceConcurrencyTests
{
    [Fact]
    public async Task Inheritance_TwoItems_SaveOne_ReadOnlyReload_Conflict_AndOriginalPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedExperienceTechnicalProposalAsync();
        await using var setup = fixture.Database.CreateDbContext();
        var user = await setup.Users.SingleAsync(x => x.Id == seed.UserId, ct);
        var project = await setup.Projects.SingleAsync(ct);
        const string original = "{\"acustico\":5,\"termico\":4,\"seguridad\":5,\"uv\":\"ns\",\"estetica\":5}";
        var source = new PublicExperienceSubmission
        {
            SubmissionId = "preferences-fixture", SchemaVersion = "sng-public-experience-v1", Source = "SNG_WEB",
            ClientFullName = "Fixture", ClientWhatsapp = "fixture", AdvisorCode = "fixture", AdvisorDisplayName = "Fixture",
            ProjectName = "Fixture", ProjectCity = "Fixture", AnswersJson = original,
            PayloadFingerprint = new string('A', 64), CreatedAtUtc = At, UpdatedAtUtc = At
        };
        source.BeginProcessing(At);
        source.CompleteProcessing(project.ClientId, project.Id, At);
        setup.Add(source);
        await setup.SaveChangesAsync(ct);
        var storedOriginal = await setup.Set<PublicExperienceSubmission>().AsNoTracking().Select(x => x.AnswersJson).SingleAsync(ct);
        var auth = Auth(user);

        async Task<RequirementExperienceDraftsResponse> Read(Guid? proposalId = null)
        {
            await using var db = fixture.Database.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            var result = await new GetRequirementExperienceDraftsService(new RequirementRepository(db), auth.Identity,
                auth.Current, new PublicExperienceSubmissionRepository(db)).ExecuteAsync(proposalId ?? seed.TechnicalProposalId, ct);
            Assert.True(result.IsSuccess);
            var repository = new RequirementRepository(db);
            var systems = Substitute.For<IProductSystemCatalogRepository>();
            var glasses = Substitute.For<IGlassTypeCatalogRepository>();
            var resolver = new RequirementExperienceLevel2Resolver(new SgProductSystemConstraintEvaluator(TimeProvider.System),
                new GlassCandidateResolver(), new DeterministicSgTechnicalSelector(systems));
            var graph = (await repository.FindTechnicalProposalForUpdateAsync(proposalId ?? seed.TechnicalProposalId, ct))!;
            foreach (var displayed in result.Value!.Items)
            {
                var consumed = await new GetRequirementExperienceLevel2Service(repository, auth.Identity, auth.Current,
                    systems, glasses, resolver, new PublicExperienceSubmissionRepository(db))
                    .ExecuteAsync(graph.Id, displayed.TechnicalProposalItemId, ct);
                var expected = await resolver.ResolveAsync(graph, graph.Items.Single(x => x.Id == displayed.TechnicalProposalItemId),
                    displayed.Level1Resolution, null, [], [], ct);
                Assert.True(consumed.IsSuccess);
                Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(consumed.Value));
            }
            Assert.False(db.ChangeTracker.HasChanges());
            return result.Value!;
        }

        var before = await Read();
        Assert.All(before.Items, x =>
        {
            Assert.Equal("EXPERIENCE", x.PreferenceOrigin);
            Assert.False(x.HasSavedDraft);
            Assert.Equal("ACOUSTIC_HIGH", x.Answers.Single(a => a.BenefitCode == "ACOUSTIC").OptionCode);
            Assert.Equal("UV_YES", x.Answers.Single(a => a.BenefitCode == "UV").OptionCode);
            Assert.Equal("AESTHETICS_HIGH", x.Answers.Single(a => a.BenefitCode == "AESTHETICS").OptionCode);
            Assert.Equal(5, x.Answers.Count);
            Assert.True(x.Level1Resolution!.IsComplete);
        });
        var accepted = before.Items.First(x => x.TechnicalProposalItemId == seed.FirstProposalItemId);
        var answers = accepted.Answers.Select(x => new UpdateRequirementExperienceAnswerRequest(x.BenefitCode, x.OptionCode)).ToArray();
        async Task<RequirementExperienceResult<RequirementExperienceItemDraftResponse>> Save(long revision, IReadOnlyList<UpdateRequirementExperienceAnswerRequest> values)
        {
            await using var db = fixture.Database.CreateDbContext();
            return await new UpdateRequirementExperienceDraftService(new RequirementRepository(db),
                new RequirementExperienceCatalogProvider(), auth.Identity, auth.Current, TimeProvider.System)
                .ExecuteAsync(seed.TechnicalProposalId, seed.FirstProposalItemId,
                    new(RequirementExperienceCatalog.V4Version, null, revision, values), ct);
        }
        var acceptance = await Save(0, answers);
        Assert.True(acceptance.IsSuccess);
        Assert.Equal("SAVED", acceptance.Value!.PreferenceOrigin);
        var changed = answers.Select(x => x.BenefitCode == "ACOUSTIC" ? x with { OptionCode = "ACOUSTIC_MEDIUM" } : x).ToArray();
        Assert.True((await Save(1, changed)).IsSuccess);
        Assert.Equal(RequirementExperienceFailure.Conflict, (await Save(1, answers)).Error);
        var after = await Read();
        Assert.Equal("ACOUSTIC_MEDIUM", after.Items.Single(x => x.TechnicalProposalItemId == seed.FirstProposalItemId)
            .Answers.Single(x => x.BenefitCode == "ACOUSTIC").OptionCode);
        Assert.Equal("EXPERIENCE", after.Items.Single(x => x.TechnicalProposalItemId == seed.SecondProposalItemId).PreferenceOrigin);
        var oldRequirement = await setup.Requirements.SingleAsync(x => x.Id == seed.RequirementId, ct);
        oldRequirement.StartProcessing(At.AddMinutes(10));
        oldRequirement.MarkProcessed(At.AddMinutes(20));
        var replacement = Requirement.Create(oldRequirement.PreQuoteId, user.Id, RequirementCommercialLine.Essential, At.AddHours(1));
        oldRequirement.SupersedeBy(replacement.Id, At.AddHours(1));
        replacement.MarkAsReplacementOf(oldRequirement.Id);
        var attempt = RequirementProcessingAttempt.Create(replacement.Id, user.Id, Guid.NewGuid(), At.AddHours(1));
        attempt.Start(At.AddHours(1));
        attempt.Complete(DocumentProcessingOutcome.Completed, At.AddHours(1));
        var extraction = RequirementExtractionResult.Create(attempt.Id, "3.0", "AI2", "{}", 1, 0, 0, 0, "fixture", 100, At.AddHours(1));
        var extracted = CreateExtractedItem(extraction.Id, 1, "V-01");
        var proposal = RequirementTechnicalProposal.Create(replacement.Id, extraction.Id, attempt.Id, false, At.AddHours(1));
        proposal.AddItem(CreateProposalItem(proposal.Id, extracted.Id));
        setup.AddRange(replacement, attempt, extraction, extracted, proposal);
        await setup.SaveChangesAsync(ct);
        var nextVersion = Assert.Single((await Read(proposal.Id)).Items);
        Assert.Equal("EXPERIENCE", nextVersion.PreferenceOrigin);
        Assert.Equal("ACOUSTIC_HIGH", nextVersion.Answers.Single(x => x.BenefitCode == "ACOUSTIC").OptionCode);
        Assert.NotEqual(seed.FirstProposalItemId, nextVersion.TechnicalProposalItemId);
        Assert.True((await Save(2, [])).IsSuccess);
        Assert.Empty((await Read()).Items.Single(x => x.TechnicalProposalItemId == seed.FirstProposalItemId).Answers);
        setup.Add(RequirementItemExperienceDraft.Create(seed.TechnicalProposalId, seed.SecondProposalItemId,
            RequirementExperienceCatalog.V3Version, null, [new("ACOUSTIC", "ACOUSTIC_2")], user.Id, At));
        await setup.SaveChangesAsync(ct);
        var historical = (await Read()).Items.Single(x => x.TechnicalProposalItemId == seed.SecondProposalItemId);
        Assert.Equal("ACOUSTIC_2", Assert.Single(historical.Answers).OptionCode);
        await using (var write = fixture.Database.CreateDbContext())
        {
            var rejected = await new UpdateRequirementExperienceDraftService(new RequirementRepository(write),
                new RequirementExperienceCatalogProvider(), auth.Identity, auth.Current, TimeProvider.System)
                .ExecuteAsync(seed.TechnicalProposalId, seed.SecondProposalItemId,
                    new(RequirementExperienceCatalog.V4Version, null, 1, answers), ct);
            Assert.Equal(RequirementExperienceFailure.UnknownCatalogVersion, rejected.Error);
            Assert.False(write.ChangeTracker.HasChanges());
        }
        Assert.Equal(storedOriginal, await setup.Set<PublicExperienceSubmission>().AsNoTracking().Select(x => x.AnswersJson).SingleAsync(ct));
        Assert.Equal(2, await setup.RequirementItemExperienceDrafts.CountAsync(ct));
    }
}
