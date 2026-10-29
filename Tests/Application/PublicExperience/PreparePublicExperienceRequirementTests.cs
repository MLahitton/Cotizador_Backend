using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PublicExperience;

public sealed class PreparePublicExperienceRequirementTests
{
    [Fact]
    public async Task UsesReservedSerialAndPersistedProjectOwner_WithoutOwnerEmailOrCurrentUser()
    {
        var submission = ProcessPublicExperienceSubmissionTests.Submission(withFile: false);
        var project = global::Domain.Projects.Project.Create(Guid.NewGuid(), "PROJECT", "Project", null, "City",
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        submission.BeginProcessing(DateTimeOffset.UtcNow);
        submission.CompleteProcessing(project.ClientId, project.Id, DateTimeOffset.UtcNow);
        byte[] bytes = [1, 2, 3, 4];
        submission.Files.Add(new()
        {
            SubmissionId = submission.Id, Sequence = 0, OriginalFileName = "original.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length, StorageKey = "public-experience/original.pdf",
            ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
        });
        var storage = new IntakeStorage();
        storage.Files[submission.Files[0].StorageKey] = bytes;
        var projects = Substitute.For<IProjectRepository>();
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        preQuotes.ReserveNextSerialAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns("PC-2026-0042");
        var requirements = Substitute.For<IRequirementRepository>();
        var service = new PreparePublicExperienceRequirementService(new PreparationMemoryRepository(submission, projects, preQuotes, requirements),
            storage, Copy(), TimeProvider.System,
            NullLogger<PreparePublicExperienceRequirementService>.Instance);
        var result = await service.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.True(result!.IsSuccess);
        await preQuotes.Received(1).ReserveNextSerialAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        preQuotes.Received(1).Add(Arg.Is<PreQuote>(x => x.Serial == "PC-2026-0042" && x.Name == null
            && x.CreatedByUserId == project.CreatedByUserId && x.ProjectId == project.Id));
        requirements.Received(1).Add(Arg.Is<Requirement>(x => x.PreQuoteId == result.PreQuoteId
            && x.CreatedByUserId == project.CreatedByUserId && x.CommercialLine == RequirementCommercialLine.Essential));
    }

    [Theory]
    [InlineData("missing-links")]
    [InlineData("missing-project")]
    [InlineData("inactive-project")]
    [InlineData("wrong-client")]
    public async Task PreconditionsFailBeforeSerialOrStorage(string scenario)
    {
        var submission = ProcessPublicExperienceSubmissionTests.Submission();
        var project = global::Domain.Projects.Project.Create(Guid.NewGuid(), "PROJECT", "Project", null, "City",
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (scenario != "missing-links")
        {
            submission.BeginProcessing(DateTimeOffset.UtcNow);
            submission.CompleteProcessing(scenario == "wrong-client" ? Guid.NewGuid() : project.ClientId,
                project.Id, DateTimeOffset.UtcNow);
        }
        if (scenario == "inactive-project") project.SetActive(false, project.CreatedByUserId, DateTimeOffset.UtcNow);
        var projects = Substitute.For<IProjectRepository>();
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "missing-project" ? null : project);
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var storage = new IntakeStorage();
        var service = new PreparePublicExperienceRequirementService(new PreparationMemoryRepository(submission, projects, preQuotes, Substitute.For<IRequirementRepository>()),
            storage, Copy(), TimeProvider.System,
            NullLogger<PreparePublicExperienceRequirementService>.Instance);
        var result = await service.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.False(result!.IsSuccess);
        Assert.Equal(scenario == "missing-links" ? PublicExperienceRequirementPreparationErrors.NotReady
            : PublicExperienceRequirementPreparationErrors.InvalidProject, result.ErrorCode);
        Assert.Empty(preQuotes.ReceivedCalls());
        Assert.Equal(0, storage.Writes);
    }

    internal static global::Application.Common.Abstractions.Storage.IVerifiedContentCopy Copy() =>
        new global::Infrastructure.Storage.VerifiedContentCopy(NullLogger<global::Infrastructure.Storage.VerifiedContentCopy>.Instance);

    internal sealed class PreparationMemoryRepository(PublicExperienceSubmission submission,
        IProjectRepository projects, IPreQuoteRepository preQuotes, IRequirementRepository requirements)
        : IPublicExperienceRequirementPreparationRepository
    {
        public async Task<PublicPreparationRead> ReadAsync(string id, CancellationToken token)
        {
            if (submission.RequirementId is not null)
                return new(null, new(id, submission.PreQuoteId, submission.RequirementId, null, true));
            var project = submission.ProjectId is null ? null : await projects.FindByIdAsync(submission.ProjectId.Value, token);
            var error = submission.ClientId is null || submission.ProjectId is null
                ? PublicExperienceRequirementPreparationErrors.NotReady
                : project is null || !project.IsActive || project.ClientId != submission.ClientId
                    ? PublicExperienceRequirementPreparationErrors.InvalidProject : null;
            return new(new(submission.Id, id, submission.ClientId, submission.ProjectId, project?.CreatedByUserId,
                submission.Status, submission.PayloadFingerprint, submission.AnswersJson,
                submission.Files.OrderBy(f => f.Sequence).Select(f => new PublicPreparationFile(f.Id, f.Sequence,
                    f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.ContentHash, f.CreatedAtUtc,
                    f.StoredLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion)).ToArray(), error), null);
        }

        public async Task<PublicExperienceRequirementPreparationResult> CommitAsync(PublicPreparationSnapshot snapshot,
            Requirement candidate, IReadOnlyList<PublicPreparedFile> files, CancellationToken token)
        {
            var serial = await preQuotes.ReserveNextSerialAsync(candidate.CreatedAtUtc, token);
            preQuotes.Add(PreQuote.Create(snapshot.ProjectId!.Value, snapshot.OwnerId!.Value, serial,
                null, candidate.CreatedAtUtc, candidate.PreQuoteId));
            requirements.Add(candidate);
            for (var i = 0; i < files.Count; i++)
            {
                var source = snapshot.Files[i];
                requirements.AddFile(RequirementFile.CreateWithStoredEvidence(candidate.Id, source.OriginalFileName,
                    source.ContentType, source.SizeBytes, files[i].StorageKey, candidate.CreatedAtUtc.AddTicks(i * 10L),
                    files[i].Receipt.Sha256, files[i].Receipt.Length, files[i].EvidenceAtUtc, 1));
            }
            submission.CompleteRequirementPreparation(candidate.PreQuoteId, candidate.Id, DateTimeOffset.UtcNow);
            return new(snapshot.SubmissionId, candidate.PreQuoteId, candidate.Id, null);
        }

        public Task<PublicExperienceRequirementPreparationResult> FailAsync(PublicPreparationSnapshot snapshot,
            string code, CancellationToken token)
        {
            submission.FailRequirementPreparation(code, DateTimeOffset.UtcNow);
            return Task.FromResult(new PublicExperienceRequirementPreparationResult(snapshot.SubmissionId, null, null, code));
        }
    }
}
