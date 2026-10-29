using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

// Share the existing structured-extraction fixtures without duplicating the pipeline setup.
public sealed partial class ProcessRequirementServiceTests
{
    [Theory]
    [InlineData("ai2")]
    [InlineData("storage")]
    public async Task Core_RequestAborted_FinalizesProcessingBeforePropagatingCancellation(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var file = File("source.pdf", PdfContentType);
        var context = CreateContext("success", file);
        RequirementProcessingAttempt? attempt = null;
        context.Requirements.When(x => x.AddProcessingAttempt(Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => attempt = call.Arg<RequirementProcessingAttempt>());
        if (stage == "ai2")
            context.Ai2.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ => { cancellation.Cancel(); return Task.FromCanceled<DocumentProcessingClientResult>(cancellation.Token); });
        else if (stage == "storage")
            context.Storage.OpenReadAsync(file.StorageKey, Arg.Any<CancellationToken>())
                .Returns(_ => { cancellation.Cancel(); return Task.FromCanceled<Stream>(cancellation.Token); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Core.ExecuteAsync(context.Requirement.Id, UserId, cancellation.Token));

        Assert.Equal(RequirementStatus.Pending, context.Requirement.Status);
        Assert.NotNull(attempt);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Cancelled, attempt.Outcome);
        await context.Requirements.Received(1).FinalizeProcessingCancellationAsync(
            context.Requirement.Id, attempt.Id, Arg.Any<DateTimeOffset>(),
            Arg.Is<CancellationToken>(token => !token.CanBeCanceled), Arg.Any<Guid?>());
        context.CancellationRegistry.Received(1).Complete(Arg.Any<string>());
        context.Requirements.DidNotReceive().AddTechnicalProposal(Arg.Any<RequirementTechnicalProposal>());
        context.Requirements.DidNotReceive().RemoveFile(Arg.Any<RequirementFile>());
    }

    [Fact]
    public async Task Core_StartAborted_DoesNotCallProviderOrCompensatingFinalizer()
    {
        using var cancellation = new CancellationTokenSource();
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.Requirements.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Core.ExecuteAsync(context.Requirement.Id, UserId, cancellation.Token));
        Assert.DoesNotContain(context.Requirements.ReceivedCalls(), x => x.GetMethodInfo().Name.StartsWith("FinalizeProcessing"));
        Assert.Empty(context.Ai2.ReceivedCalls());
        context.CancellationRegistry.Received(1).Complete(Arg.Any<string>());
    }

    [Fact]
    public async Task Core_ExplicitOwner_ProcessesOnceAndBuildsStructuredProposal()
    {
        var file = File("source.pdf", PdfContentType);
        var context = CreateContext("success", file);
        context.CurrentUser.IsAuthenticated.Returns(false);
        context.CurrentUser.ClearReceivedCalls();
        RequirementProcessingAttempt? attempt = null;
        RequirementExtractedItem? item = null;
        RequirementTechnicalProposal? proposal = null;
        context.Requirements.When(x => x.AddProcessingAttempt(Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => attempt = call.Arg<RequirementProcessingAttempt>());
        context.Requirements.When(x => x.AddExtractedItem(Arg.Any<RequirementExtractedItem>()))
            .Do(call => item = call.Arg<RequirementExtractedItem>());
        context.Requirements.When(x => x.AddTechnicalProposal(Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Core.ExecuteAsync(context.Requirement.Id, UserId, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        Assert.NotNull(attempt);
        Assert.Equal(UserId, attempt.RequestedByUserId);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Completed, attempt.Outcome);
        context.Requirements.Received(1).AddExtractionResult(Arg.Any<RequirementExtractionResult>());
        Assert.NotNull(item);
        Assert.Equal("element-pv06", item.Ai2ElementId);
        Assert.NotNull(proposal);
        Assert.Equal(item.Id, Assert.Single(proposal.Items).RequirementExtractedItemId);
        await context.Ai2.Received(1).ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>());
        await context.Storage.Received(1).OpenReadAsync(file.StorageKey, Arg.Any<CancellationToken>());
        Assert.Empty(context.CurrentUser.ReceivedCalls());
    }

    [Theory]
    [InlineData(false, "missing", ProcessRequirementFailure.Unauthorized)]
    [InlineData(true, "missing", ProcessRequirementFailure.Unauthorized)]
    [InlineData(false, "inactive", ProcessRequirementFailure.InactiveUser)]
    [InlineData(true, "inactive", ProcessRequirementFailure.InactiveUser)]
    [InlineData(false, "other-owner", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData(true, "other-owner", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData(false, "identity-error", ProcessRequirementFailure.QueryError)]
    [InlineData(true, "identity-error", ProcessRequirementFailure.QueryError)]
    public async Task CoreAndWrapper_IdentityAndOwnershipRemainRequired(bool direct, string scenario, ProcessRequirementFailure expected)
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        var actor = scenario == "other-owner" ? Guid.NewGuid() : UserId;
        context.CurrentUser.UserId.Returns(actor);
        if (scenario == "missing")
            context.Identity.FindUserByIdAsync(actor, Arg.Any<CancellationToken>()).Returns((global::Domain.Identity.User?)null);
        else if (scenario == "identity-error")
            context.Identity.FindUserByIdAsync(actor, Arg.Any<CancellationToken>())
                .Returns(Task.FromException<global::Domain.Identity.User?>(new IOException("identity unavailable")));
        else
        {
            if (scenario == "inactive") context.User.Deactivate(At.AddMinutes(1));
            context.Identity.FindUserByIdAsync(actor, Arg.Any<CancellationToken>()).Returns(context.User);
        }
        var result = direct
            ? await context.Core.ExecuteAsync(context.Requirement.Id, actor, TestContext.Current.CancellationToken)
            : await context.Service.ExecuteAsync(new(context.Requirement.Id), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Failure);
        Assert.Empty(context.Ai2.ReceivedCalls());
        context.Requirements.DidNotReceive().AddProcessingAttempt(Arg.Any<RequirementProcessingAttempt>());
    }

    [Fact]
    public async Task Wrapper_Unauthenticated_DoesNotQueryIdentityOrRunPipeline()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.CurrentUser.IsAuthenticated.Returns(false);
        var result = await context.Service.ExecuteAsync(new(context.Requirement.Id), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessRequirementFailure.Unauthorized, result.Failure);
        Assert.Empty(context.Identity.ReceivedCalls());
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Fact]
    public async Task Wrapper_InvalidCommand_StillReturnsInvalidRequestFirst()
    {
        var context = CreateContext("success");
        context.CurrentUser.IsAuthenticated.Returns(false);
        var result = await context.Service.ExecuteAsync(new(Guid.Empty), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessRequirementFailure.InvalidRequest, result.Failure);
        Assert.Empty(context.Identity.ReceivedCalls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoreAndWrapper_AiRejected_PreservesFailureCode(bool direct)
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.Ai2.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.RemoteFailure(DocumentProcessingClientFailure.RemoteRejection,
                new DocumentProcessingRemoteError(422, "AI2-1.0", "AI2_REQUEST_REJECTED", "Rejected")));
        var result = direct
            ? await context.Core.ExecuteAsync(context.Requirement.Id, UserId, TestContext.Current.CancellationToken)
            : await context.Service.ExecuteAsync(new(context.Requirement.Id), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessRequirementFailure.AiRemoteRejected, result.Failure);
        await context.Requirements.Received(1).FinalizeProcessingFailureAsync(context.Requirement.Id,
            Arg.Any<Guid>(), "AI2_REQUEST_REJECTED", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<Guid?>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoreAndWrapper_PersistenceFailure_PreservesFailure(bool direct)
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.Requirements.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("DB")));
        var result = direct
            ? await context.Core.ExecuteAsync(context.Requirement.Id, UserId, TestContext.Current.CancellationToken)
            : await context.Service.ExecuteAsync(new(context.Requirement.Id), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessRequirementFailure.PersistenceError, result.Failure);
        Assert.Empty(context.Ai2.ReceivedCalls());
    }

    [Theory]
    [InlineData("not_found", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData("already_processing", ProcessRequirementFailure.AlreadyProcessing)]
    [InlineData("no_files", ProcessRequirementFailure.NoFiles)]
    [InlineData("storage_error", ProcessRequirementFailure.StorageError)]
    [InlineData("ai_unavailable", ProcessRequirementFailure.AiServiceUnavailable)]
    [InlineData("ai_timeout", ProcessRequirementFailure.AiTimeout)]
    [InlineData("ai_invalid", ProcessRequirementFailure.AiInvalidResponse)]
    public async Task Core_DirectFailures_KeepExistingSemantics(string scenario, ProcessRequirementFailure expected)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        var result = await context.Core.ExecuteAsync(context.Requirement.Id, UserId, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Failure);
    }
}
