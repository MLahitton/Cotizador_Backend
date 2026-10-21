using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CancelRequirementProcessingAttemptServiceTests
{
    public static readonly DateTimeOffset At = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ActiveOwner_CancelsEvenWhenAttemptWasStartedByAnotherActor(bool byRequirement)
    {
        var context = Create();
        var result = await Execute(context, byRequirement);
        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingState.Finished, result.Attempt!.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Cancelled, result.Attempt.Outcome);
        context.Registry.Received(1).TryCancel(RequirementOperationKeys.ProcessingAttempt(context.Attempt.Id));
        await context.Requirements.Received(1).FinalizeProcessingCancellationAsync(
            context.Requirement.Id, context.Attempt.Id, At.AddMinutes(2), Arg.Any<CancellationToken>());
        Assert.NotEqual(context.User.Id, context.Attempt.RequestedByUserId);
    }

    public static IEnumerable<object[]> DeniedCases()
    {
        foreach (var byRequirement in new[] { false, true })
        foreach (var scenario in new[] { "other", "admin-other", "inactive", "missing-user", "anonymous", "missing-user-id", "finished-other" })
            yield return [byRequirement, scenario];
    }

    [Theory]
    [MemberData(nameof(DeniedCases))]
    public async Task DeniedUsers_NeverCancelFinalizeOrSeeAttempt(bool byRequirement, string scenario)
    {
        var context = Create(scenario);
        var result = await Execute(context, byRequirement);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Attempt);
        Assert.Equal(scenario switch
        {
            "inactive" => CancelRequirementProcessingAttemptFailure.InactiveUser,
            "anonymous" or "missing-user" or "missing-user-id" => CancelRequirementProcessingAttemptFailure.Unauthorized,
            _ => CancelRequirementProcessingAttemptFailure.ProcessingAttemptNotFound
        }, result.Failure);
        AssertNoMutation(context);
        if (scenario is "inactive" or "anonymous" or "missing-user" or "missing-user-id")
            Assert.Empty(context.Requirements.ReceivedCalls());
        if (scenario is "other" or "admin-other" or "finished-other")
            Assert.Equal(context.User.Id, context.Attempt.RequestedByUserId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FinishedOwnerAttempt_ReturnsExistingOutcomeWithoutMutation(bool byRequirement)
    {
        var context = Create();
        context.Attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(1));
        var result = await Execute(context, byRequirement);
        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.Completed, result.Attempt!.Outcome);
        AssertNoMutation(context);
        await context.Projects.Received(1).FindByIdAsync(context.Project.Id, Arg.Any<CancellationToken>());
    }

    public static IEnumerable<object[]> LookupCases()
    {
        foreach (var byRequirement in new[] { false, true })
        foreach (var lookup in new[] { "attempt", "requirement", "prequote", "project", "identity" })
        foreach (var fail in new[] { false, true })
            if (lookup != "identity" || fail) yield return [byRequirement, lookup, fail];
    }

    [Theory]
    [MemberData(nameof(LookupCases))]
    public async Task MissingLinksAndQueryErrors_FailClosed(bool byRequirement, string lookup, bool fail)
    {
        var context = Create();
        var exception = new InvalidOperationException("PRIVATE_DATABASE_DETAIL");
        if (lookup == "identity")
            context.Identity.FindUserByIdAsync(context.User.Id, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
        if (lookup == "attempt")
        {
            if (fail)
            {
                context.Requirements.FindProcessingAttemptByIdAsync(context.Attempt.Id, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
                context.Requirements.FindActiveProcessingAttemptByRequirementIdAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
            }
            else
            {
                context.Requirements.FindProcessingAttemptByIdAsync(context.Attempt.Id, Arg.Any<CancellationToken>()).Returns((RequirementProcessingAttempt?)null);
                context.Requirements.FindActiveProcessingAttemptByRequirementIdAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).Returns((RequirementProcessingAttempt?)null);
            }
        }
        if (lookup == "requirement")
        {
            if (fail) context.Requirements.FindByIdAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
            else context.Requirements.FindByIdAsync(context.Requirement.Id, Arg.Any<CancellationToken>()).Returns((Requirement?)null);
        }
        if (lookup == "prequote")
        {
            if (fail) context.PreQuotes.FindByIdAsync(context.Requirement.PreQuoteId, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
            else context.PreQuotes.FindByIdAsync(context.Requirement.PreQuoteId, Arg.Any<CancellationToken>()).Returns((PreQuoteDetails?)null);
        }
        if (lookup == "project")
        {
            if (fail) context.Projects.FindByIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).ThrowsAsync(exception);
            else context.Projects.FindByIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).Returns((global::Domain.Projects.Project?)null);
        }
        var result = await Execute(context, byRequirement);
        Assert.Equal(fail ? CancelRequirementProcessingAttemptFailure.QueryError
            : CancelRequirementProcessingAttemptFailure.ProcessingAttemptNotFound, result.Failure);
        Assert.Null(result.Attempt);
        AssertNoMutation(context);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PersistenceFailure_RemainsPersistenceError(bool byRequirement)
    {
        var context = Create();
        context.Requirements.FinalizeProcessingCancellationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("private"));
        var result = await Execute(context, byRequirement);
        Assert.Equal(CancelRequirementProcessingAttemptFailure.PersistenceError, result.Failure);
        Assert.Null(result.Attempt);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InvalidId_DoesNotQueryOrMutate(bool byRequirement)
    {
        var context = Create();
        var result = byRequirement
            ? await context.Service.ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(Guid.Empty), TestContext.Current.CancellationToken)
            : await context.Service.ExecuteAsync(new CancelRequirementProcessingAttemptCommand(Guid.Empty), TestContext.Current.CancellationToken);
        Assert.Equal(CancelRequirementProcessingAttemptFailure.InvalidRequest, result.Failure);
        Assert.Empty(context.Identity.ReceivedCalls());
        AssertNoMutation(context);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelledQuery_PropagatesWithoutMutation(bool byRequirement)
    {
        var context = Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        context.Identity.FindUserByIdAsync(context.User.Id, Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return Task.FromCanceled<User?>(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => byRequirement
            ? context.Service.ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(context.Requirement.Id), cancellation.Token)
            : context.Service.ExecuteAsync(new CancelRequirementProcessingAttemptCommand(context.Attempt.Id), cancellation.Token));
        AssertNoMutation(context);
    }

    public static Task<CancelRequirementProcessingAttemptResult> Execute(Context context, bool byRequirement) => byRequirement
        ? context.Service.ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(context.Requirement.Id), TestContext.Current.CancellationToken)
        : context.Service.ExecuteAsync(new CancelRequirementProcessingAttemptCommand(context.Attempt.Id), TestContext.Current.CancellationToken);

    public static void AssertNoMutation(Context context)
    {
        Assert.Empty(context.Registry.ReceivedCalls());
        Assert.DoesNotContain(context.Requirements.ReceivedCalls(), call =>
            !call.GetMethodInfo().Name.StartsWith("Find", StringComparison.Ordinal));
    }

    public static Context Create(string scenario = "owner")
    {
        var current = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var registry = Substitute.For<IOperationCancellationRegistry>();
        var user = User.CreateFromGoogle("owner@example.test", "Owner", null, null, At);
        if (scenario == "admin-other") user.ChangeRole(UserRole.Admin, At);
        if (scenario == "inactive") user.Deactivate(At);
        var other = scenario is "other" or "admin-other" or "finished-other";
        var project = global::Domain.Projects.Project.Create(Guid.NewGuid(), "QA-1", "Project", null, null, other ? Guid.NewGuid() : user.Id, At);
        var requirement = Requirement.Create(Guid.NewGuid(), project.CreatedByUserId, RequirementCommercialLine.Classic, At);
        requirement.StartProcessing(At);
        var attempt = RequirementProcessingAttempt.Create(requirement.Id, other ? user.Id : Guid.NewGuid(), Guid.NewGuid(), At);
        attempt.Start(At);
        if (scenario == "finished-other") attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(1));
        current.IsAuthenticated.Returns(scenario != "anonymous");
        current.UserId.Returns(scenario == "missing-user-id" ? (Guid?)null : user.Id);
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(scenario == "missing-user" ? null : user);
        requirements.FindProcessingAttemptByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        requirements.FindActiveProcessingAttemptByRequirementIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        requirements.FindByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(requirement);
        preQuotes.FindByIdAsync(requirement.PreQuoteId, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(requirement.PreQuoteId, project.Id, 0, At, At));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        requirements.FinalizeProcessingCancellationAsync(requirement.Id, attempt.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementProcessingCancellationFinalization(requirement.Id, attempt.Id, attempt.CorrelationId,
                DocumentProcessingState.Finished, DocumentProcessingOutcome.Cancelled, At, At.AddMinutes(2)));
        return new(new(current, identity, preQuotes, projects, requirements, registry, new FixedTimeProvider()),
            current, identity, requirements, preQuotes, projects, registry, user, project, requirement, attempt);
    }

    public sealed record Context(CancelRequirementProcessingAttemptService Service, ICurrentUser Current,
        IIdentityRepository Identity, IRequirementRepository Requirements, IPreQuoteRepository PreQuotes,
        IProjectRepository Projects, IOperationCancellationRegistry Registry, User User,
        global::Domain.Projects.Project Project, Requirement Requirement, RequirementProcessingAttempt Attempt);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At.AddMinutes(2);
    }
}
