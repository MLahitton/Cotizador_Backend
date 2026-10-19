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
        var service = new PreparePublicExperienceRequirementService(new PreparationMemoryRepository(submission),
            projects, preQuotes, requirements, storage, TimeProvider.System,
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
        var service = new PreparePublicExperienceRequirementService(new PreparationMemoryRepository(submission),
            projects, preQuotes, Substitute.For<IRequirementRepository>(), storage, TimeProvider.System,
            NullLogger<PreparePublicExperienceRequirementService>.Instance);
        var result = await service.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.False(result!.IsSuccess);
        Assert.Equal(scenario == "missing-links" ? PublicExperienceRequirementPreparationErrors.NotReady
            : PublicExperienceRequirementPreparationErrors.InvalidProject, result.ErrorCode);
        Assert.Empty(preQuotes.ReceivedCalls());
        Assert.Equal(0, storage.Writes);
    }

    private sealed class PreparationMemoryRepository(PublicExperienceSubmission submission)
        : IPublicExperienceRequirementPreparationRepository
    {
        public async Task<PublicExperienceRequirementPreparationResult?> PrepareLockedAsync(string id,
            Func<PublicExperienceSubmission, CancellationToken, Task> prepare, Func<Task> compensate, CancellationToken token)
        {
            try { await prepare(submission, token); }
            catch (PublicExperienceRequirementPreparationException exception)
            {
                await compensate();
                submission.FailRequirementPreparation(exception.ErrorCode, DateTimeOffset.UtcNow);
            }
            return new(id, submission.PreQuoteId, submission.RequirementId, submission.RequirementPreparationErrorCode);
        }
    }
}
