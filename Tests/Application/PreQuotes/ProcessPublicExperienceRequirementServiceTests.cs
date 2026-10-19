using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed partial class ProcessRequirementServiceTests
{
    [Theory]
    [InlineData("DIRECT_CONTACT", "success")]
    [InlineData("ANDRES_CANDELA", "requires_review")]
    public async Task PublicAdapter_Pending_UsesPersistedOwnerAndReplaysWithoutAi(string advisor, string scenario)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context, advisor);
        context.CurrentUser.ClearReceivedCalls();

        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(scenario == "requires_review" ? PublicExperienceRequirementProcessingState.RequiresReview
            : PublicExperienceRequirementProcessingState.Completed, result.State);
        Assert.Equal(RequirementStatus.Processed, result.RequirementStatus);
        Assert.NotNull(result.TechnicalProposalId);
        var attempt = Assert.Single(context.Requirement.ProcessingAttempts);
        Assert.Equal(context.Project.CreatedByUserId, attempt.RequestedByUserId);
        Assert.Equal(attempt.Id, result.ProcessingAttemptId);
        context.Requirements.Received(1).AddExtractionResult(Arg.Any<RequirementExtractionResult>());
        context.Requirements.Received(1).AddTechnicalProposal(Arg.Any<RequirementTechnicalProposal>());
        Assert.Empty(context.CurrentUser.ReceivedCalls());

        var replay = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal(PublicExperienceRequirementProcessingState.AlreadyProcessed, replay.State);
        Assert.Equal(result.TechnicalProposalId, replay.TechnicalProposalId);
        await context.Ai2.Received(1).ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("already_processing", PublicExperienceRequirementProcessingState.AlreadyProcessing)]
    [InlineData("processed", PublicExperienceRequirementProcessingState.ProcessingError)]
    [InlineData("cancelled", PublicExperienceRequirementProcessingState.CancelledOrSuperseded)]
    [InlineData("superseded", PublicExperienceRequirementProcessingState.CancelledOrSuperseded)]
    [InlineData("failed", PublicExperienceRequirementProcessingState.Failed)]
    public async Task PublicAdapter_NonPending_NeverRunsCore(string scenario, PublicExperienceRequirementProcessingState expected)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context);
        if (scenario == "cancelled") context.Requirement.Cancel(At.AddMinutes(5));
        if (scenario == "superseded")
        {
            context.Requirement.StartProcessing(At.AddMinutes(2));
            context.Requirement.MarkProcessed(At.AddMinutes(3));
            context.Requirement.SupersedeBy(Guid.NewGuid(), At.AddMinutes(5));
        }
        if (scenario == "failed")
        {
            context.Requirement.StartProcessing(At.AddMinutes(2));
            context.Requirement.MarkFailed(At.AddMinutes(4));
            var attempt = RequirementProcessingAttempt.Create(context.Requirement.Id, UserId, Guid.NewGuid(), At);
            attempt.Start(At.AddMinutes(2));
            attempt.Fail("AI2_TIMEOUT", At.AddMinutes(4));
            ((ICollection<RequirementProcessingAttempt>)context.Requirement.ProcessingAttempts).Add(attempt);
        }
        context.Identity.ClearReceivedCalls();
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.State);
        if (scenario == "processed") Assert.Equal("PUBLIC_EXPERIENCE_TECHNICAL_PROPOSAL_MISSING", result.ErrorCode);
        if (scenario == "failed")
        {
            Assert.Equal("PROCESSING_FAILED", result.ReasonCode);
            Assert.Equal("AI2_TIMEOUT", result.ErrorCode);
            Assert.NotNull(result.ProcessingAttemptId);
        }
        Assert.Empty(context.Identity.ReceivedCalls());
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Fact]
    public async Task PublicAdapter_AlreadyProcessed_ReturnsExistingProposal()
    {
        var context = CreateContext("processed", File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context);
        var proposal = RequirementTechnicalProposal.Create(context.Requirement.Id, Guid.NewGuid(), Guid.NewGuid(), true, At);
        context.Requirements.GetCurrentTechnicalProposalAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).Returns(proposal);
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(PublicExperienceRequirementProcessingState.AlreadyProcessed, result.State);
        Assert.Equal(proposal.Id, result.TechnicalProposalId);
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Theory]
    [InlineData("missing-submission")]
    [InlineData("missing-project")]
    [InlineData("missing-requirement")]
    public async Task PublicAdapter_NotPrepared_DoesNotAcquireLock(string scenario)
    {
        var context = CreateContext("success");
        var submission = ProcessPublicExperienceSubmissionTests.Submission();
        if (scenario == "missing-requirement")
        {
            submission.BeginProcessing(At);
            submission.CompleteProcessing(context.Project.ClientId, context.Project.Id, At);
        }
        var repository = new IntakeRepository();
        if (scenario != "missing-submission") repository.Rows[submission.SubmissionId] = submission;
        var adapter = NewAdapter(context, repository, context.ProcessingLock);
        context.ProcessingLock.ClearReceivedCalls();
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal("PUBLIC_EXPERIENCE_REQUIREMENT_NOT_PREPARED", result.ErrorCode);
        Assert.Empty(context.ProcessingLock.ReceivedCalls());
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Theory]
    [InlineData("project")]
    [InlineData("prequote-project")]
    [InlineData("prequote")]
    [InlineData("requirement")]
    [InlineData("requirement-prequote")]
    public async Task PublicAdapter_InvalidLinks_NeverProcesses(string scenario)
    {
        var context = CreateContext("success");
        var (adapter, submission) = CreatePublicAdapter(context,
            linkedPreQuoteId: scenario == "requirement-prequote" ? Guid.NewGuid() : null);
        if (scenario == "project")
            context.Projects.FindByIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).Returns((global::Domain.Projects.Project?)null);
        if (scenario.StartsWith("prequote", StringComparison.Ordinal))
            context.PreQuotes.FindByIdAsync(context.Requirement.PreQuoteId, Arg.Any<CancellationToken>())
                .Returns(new PreQuoteDetails(scenario == "prequote" ? Guid.NewGuid() : context.Requirement.PreQuoteId,
                    Guid.NewGuid(), 0, At, At));
        if (scenario == "requirement")
            context.Requirements.FindByIdAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).Returns((Requirement?)null);
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal("PUBLIC_EXPERIENCE_PROCESSING_LINK_INVALID", result.ErrorCode);
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Theory]
    [InlineData("ai_unavailable", ProcessRequirementFailure.AiServiceUnavailable)]
    [InlineData("ai_timeout", ProcessRequirementFailure.AiTimeout)]
    [InlineData("ai_invalid", ProcessRequirementFailure.AiInvalidResponse)]
    [InlineData("ai_service_error", ProcessRequirementFailure.AiServiceError)]
    public async Task PublicAdapter_CoreFailure_PreservesDiagnosticsWithoutRetry(string scenario, ProcessRequirementFailure failure)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context);
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.CoreResult);
        Assert.Equal(failure, result.CoreResult.Failure);
        Assert.Equal(result.CoreResult.Attempt!.ErrorCode, result.ErrorCode);
        Assert.Equal(result.CoreResult.Attempt.ProcessingAttemptId, result.ProcessingAttemptId);
        var replay = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal(PublicExperienceRequirementProcessingState.Failed, replay.State);
        Assert.Equal(result.ErrorCode, replay.ErrorCode);
        await context.Ai2.Received(1).ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublicAdapter_ProposalRequiresReview_IsSuccessfulEvenWhenAiCompleted()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context);
        var reviewProposal = RequirementTechnicalProposal.Create(context.Requirement.Id, Guid.NewGuid(), Guid.NewGuid(), true, At);
        context.Requirements.GetCurrentTechnicalProposalAsync(context.Requirement.Id, Arg.Any<CancellationToken>())
            .Returns(reviewProposal);
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.ProcessingOutcome);
        Assert.Equal(PublicExperienceRequirementProcessingState.RequiresReview, result.State);
        Assert.Equal(RequirementStatus.Processed, result.RequirementStatus);
        Assert.NotNull(result.TechnicalProposalId);
    }

    [Theory]
    [InlineData("storage_error", ProcessRequirementFailure.StorageError)]
    [InlineData("persistence", ProcessRequirementFailure.PersistenceError)]
    public async Task PublicAdapter_LocalFailures_PreserveCoreResult(string scenario, ProcessRequirementFailure failure)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        var (adapter, submission) = CreatePublicAdapter(context);
        if (scenario == "persistence")
            context.Requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new IOException("DB unavailable")));
        var result = await adapter.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.CoreResult);
        Assert.Equal(failure, result.CoreResult.Failure);
        Assert.Equal(result.CoreResult.Attempt?.ProcessingAttemptId
            ?? Assert.Single(context.Requirement.ProcessingAttempts).Id, result.ProcessingAttemptId);
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Fact]
    public async Task PublicAdapter_PostgreSqlIndependentConnections_SerializeAdapterAndAuthenticatedCore()
    {
        await using var database = new PostgreSqlIntegrationFixture { UseMigrations = false };
        await database.InitializeAsync();
        database.RequireAvailable();
        await using var db1 = database.CreateDbContext();
        await using var db2 = database.CreateDbContext();
        var lock1 = new RequirementProcessingLock(db1);
        var lock2 = new RequirementProcessingLock(db2);
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        var (first, submission) = CreatePublicAdapter(context, processingLock: lock1);
        var repository = new IntakeRepository();
        repository.Rows[submission.SubmissionId] = submission;
        var second = NewAdapter(context, repository, lock2);
        context.ProcessingLock.TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => lock2.TryAcquireAsync(call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Ai2.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
                return DocumentProcessingClientResult.Success(CreateResponse(call.Arg<DocumentProcessingClientRequest>()));
            });
        var running = first.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var contender = await second.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
            Assert.Equal(PublicExperienceRequirementProcessingState.AlreadyProcessing, contender.State);
            var authenticated = await context.Core.ExecuteAsync(context.Requirement.Id, UserId, TestContext.Current.CancellationToken);
            Assert.Equal(ProcessRequirementFailure.AlreadyProcessing, authenticated.Failure);
        }
        finally
        {
            release.TrySetResult();
            await running;
        }
        Assert.True((await running).IsSuccess);
        var replay = await second.ExecuteAsync(submission.SubmissionId, TestContext.Current.CancellationToken);
        Assert.Equal(PublicExperienceRequirementProcessingState.AlreadyProcessed, replay.State);
        Assert.Equal((await running).TechnicalProposalId, replay.TechnicalProposalId);
        await context.Ai2.Received(1).ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>());
    }

    private static (ProcessPublicExperienceRequirementService Service, PublicExperienceSubmission Submission)
        CreatePublicAdapter(Context context, string advisor = "DIRECT_CONTACT", IRequirementProcessingLock? processingLock = null,
            Guid? linkedPreQuoteId = null)
    {
        var submission = ProcessPublicExperienceSubmissionTests.Submission(advisor);
        submission.BeginProcessing(At);
        submission.CompleteProcessing(context.Project.ClientId, context.Project.Id, At);
        var preQuoteId = linkedPreQuoteId ?? context.Requirement.PreQuoteId;
        submission.CompleteRequirementPreparation(preQuoteId, context.Requirement.Id, At);
        var repository = new IntakeRepository();
        repository.Rows[submission.SubmissionId] = submission;
        context.PreQuotes.FindByIdAsync(preQuoteId, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(preQuoteId, context.Project.Id, 0, At, At));
        RequirementTechnicalProposal? proposal = null;
        context.Requirements.When(x => x.AddTechnicalProposal(Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());
        context.Requirements.GetCurrentTechnicalProposalAsync(context.Requirement.Id, Arg.Any<CancellationToken>())
            .Returns(_ => proposal);
        context.Requirements.When(x => x.AddProcessingAttempt(Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => ((ICollection<RequirementProcessingAttempt>)context.Requirement.ProcessingAttempts)
                .Add(call.Arg<RequirementProcessingAttempt>()));
        return (NewAdapter(context, repository, processingLock ?? context.ProcessingLock), submission);
    }

    private static ProcessPublicExperienceRequirementService NewAdapter(Context context, IntakeRepository repository,
        IRequirementProcessingLock processingLock) => new(repository, context.Projects, context.PreQuotes,
            context.Requirements, processingLock, context.Core, NullLogger<ProcessPublicExperienceRequirementService>.Instance);
}
