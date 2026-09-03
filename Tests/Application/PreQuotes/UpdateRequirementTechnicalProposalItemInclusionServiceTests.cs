using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdateRequirementTechnicalProposalItemInclusionServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithIncludedItemExclusion_OpensTransactionAndInvalidatesConfirmation()
    {
        var context = CreateContext(confirmProposal: true);
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemInclusionCommand(
                context.Requirement.Id,
                context.Item.Id,
                false,
                "Fuera de alcance"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(context.Item.IsIncluded);
        Assert.Equal(UserId, context.Item.ExcludedByUserId);
        Assert.Equal(At, context.Item.ExcludedAtUtc);
        Assert.Equal("Fuera de alcance", context.Item.ExclusionReason);
        Assert.Equal(initialRevision + 1, context.Proposal.CommercialRevision);
        Assert.False(context.Proposal.IsCommerciallyConfirmed);
        Assert.Equal(["begin", "find", "save", "commit"], context.Calls);
    }

    [Fact]
    public async Task Execute_WithExcludedItemReactivation_IncrementsRevision()
    {
        var context = CreateContext();
        context.Item.Exclude(UserId, At.AddMinutes(-5), "Fuera de alcance");
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemInclusionCommand(
                context.Requirement.Id,
                context.Item.Id,
                true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(context.Item.IsIncluded);
        Assert.Null(context.Item.ExcludedAtUtc);
        Assert.Null(context.Item.ExcludedByUserId);
        Assert.Null(context.Item.ExclusionReason);
        Assert.Equal(initialRevision + 1, context.Proposal.CommercialRevision);
        Assert.Equal(["begin", "find", "save", "commit"], context.Calls);
    }

    [Fact]
    public async Task Execute_WithAlreadyExcludedItem_DoesNotIncrementRevision()
    {
        var context = CreateContext();
        context.Item.Exclude(UserId, At.AddMinutes(-5), "Fuera de alcance");
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemInclusionCommand(
                context.Requirement.Id,
                context.Item.Id,
                false,
                "Otro motivo"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(context.Item.IsIncluded);
        Assert.Equal(initialRevision, context.Proposal.CommercialRevision);
        Assert.Equal("Fuera de alcance", context.Item.ExclusionReason);
        Assert.Equal(["begin", "find", "save", "commit"], context.Calls);
    }

    [Fact]
    public async Task Execute_WithAlreadyIncludedItem_DoesNotIncrementRevision()
    {
        var context = CreateContext(confirmProposal: true);
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemInclusionCommand(
                context.Requirement.Id,
                context.Item.Id,
                true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(context.Item.IsIncluded);
        Assert.Equal(initialRevision, context.Proposal.CommercialRevision);
        Assert.True(context.Proposal.IsCommerciallyConfirmed);
        Assert.Equal(["begin", "find", "save", "commit"], context.Calls);
    }

    private static Context CreateContext(bool confirmProposal = false)
    {
        var validator =
            new UpdateRequirementTechnicalProposalItemInclusionCommandValidator();
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var transaction = Substitute.For<IRequirementPersistenceTransaction>();
        var calls = new List<string>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        SetPrivateProperty(user, "Id", UserId);
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
            UserId,
            At);
        var project = ProjectEntity.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            null,
            UserId,
            At);
        var preQuote = PreQuote.Create(
            project.Id,
            UserId,
            "PC-2026-0001",
            null,
            At);
        var requirement = Requirement.Create(
            preQuote.Id,
            UserId,
            RequirementCommercialLine.Essential,
            At);
        var extraction = RequirementExtractionResult.Create(
            Guid.NewGuid(),
            "1",
            "AI2",
            "{}",
            1,
            0,
            0,
            0,
            "ai2_requirement_extraction",
            100,
            At);
        var extractedItem = RequirementExtractedItem.Create(
            extraction.Id,
            "element-1",
            1,
            "PV-06",
            "Puerta vidriera",
            StructuredElementType.Door,
            1,
            3740,
            2500,
            9.35m,
            0.91m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "SLIDING_DOOR",
            "SLIDING",
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            "3831",
            "3831",
            "templado 6 mm",
            "templado",
            "templado",
            6m,
            null,
            null,
            null,
            null,
            "monolitico",
            null,
            null,
            false,
            "negro pintura al horno",
            "PAINTED",
            "negro",
            "BLACK",
            null,
            "MATTE",
            null,
            false,
            At);
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            extractedItem.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.90m,
            0.90m,
            0.90m,
            0.90m,
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
            "NotEvaluated",
            At);
        SetPrivateProperty(item, "ExtractedItem", extractedItem);
        proposal.AddItem(item);

        if (confirmProposal)
        {
            proposal.ConfirmCommercialSelection(UserId, At.AddMinutes(-1));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.BeginPricingUpdateTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("begin");
                return transaction;
            });
        requirements.FindCurrentTechnicalProposalForUpdateAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("find");
                return proposal;
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            });
        transaction.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("commit");
                return Task.CompletedTask;
            });
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var service = new UpdateRequirementTechnicalProposalItemInclusionService(
            validator,
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients,
            new FixedTimeProvider(At));

        return new Context(
            service,
            requirements,
            transaction,
            calls,
            requirement,
            proposal,
            item);
    }

    private static void SetPrivateProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private sealed record Context(
        UpdateRequirementTechnicalProposalItemInclusionService Service,
        IRequirementRepository Requirements,
        IRequirementPersistenceTransaction Transaction,
        List<string> Calls,
        Requirement Requirement,
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item);
}
