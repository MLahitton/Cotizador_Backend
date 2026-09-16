using Application.Common.Abstractions.Dashboard;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Queries;

public sealed class UserDashboardReader(
    ApplicationDbContext dbContext)
    : IUserDashboardReader
{
    public async Task<UserDashboardSnapshot> GetDashboardAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var activeProjects = await dbContext.Projects
            .AsNoTracking()
            .CountAsync(
                project =>
                    project.CreatedByUserId == userId
                    && project.IsActive,
                cancellationToken);

        var totalPreQuotes = await dbContext.PreQuotes
            .AsNoTracking()
            .CountAsync(
                preQuote =>
                    preQuote.CreatedByUserId == userId,
                cancellationToken);

        var requirementsInProgress = await dbContext.Requirements
            .AsNoTracking()
            .CountAsync(
                requirement =>
                    requirement.CreatedByUserId == userId
                    && requirement.IsActive
                    && requirement.Status
                        == RequirementStatus.Processing
                    && requirement.SupersededByRequirementId == null,
                cancellationToken);

        var proposalsRequiringReview =
            await dbContext.RequirementTechnicalProposals
                .AsNoTracking()
                .Where(proposal =>
                    proposal.Status
                        == RequirementTechnicalProposalStatus.RequiresReview
                    && proposal.Requirement.CreatedByUserId == userId
                    && proposal.Requirement.IsActive
                    && proposal.Requirement.Status
                        != RequirementStatus.Cancelled
                    && proposal.Requirement.Status
                        != RequirementStatus.Superseded
                    && proposal.Requirement.SupersededByRequirementId == null
                    && !dbContext.RequirementTechnicalProposals.Any(
                        newer =>
                            newer.RequirementId
                                == proposal.RequirementId
                            && (
                                newer.CreatedAtUtc
                                    > proposal.CreatedAtUtc
                                || (
                                    newer.CreatedAtUtc
                                        == proposal.CreatedAtUtc
                                    && newer.Id
                                        .CompareTo(proposal.Id) > 0
                                )
                            )))
                .CountAsync(cancellationToken);

        var recentProjects = await dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.CreatedByUserId == userId)
            .OrderByDescending(project =>
                project.UpdatedAtUtc)
            .ThenByDescending(project =>
                project.CreatedAtUtc)
            .Take(5)
            .Select(project =>
                new UserDashboardRecentProjectSnapshot(
                    project.Id,
                    project.Code,
                    project.Name,
                    project.ClientId,
                    project.Client.TradeName
                        ?? project.Client.LegalName,
                    project.IsActive,
                    project.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var proposalReviewItems =
            await dbContext.RequirementTechnicalProposals
                .AsNoTracking()
                .Where(proposal =>
                    proposal.Status
                        == RequirementTechnicalProposalStatus.RequiresReview
                    && proposal.Requirement.CreatedByUserId == userId
                    && proposal.Requirement.IsActive
                    && proposal.Requirement.Status
                        != RequirementStatus.Cancelled
                    && proposal.Requirement.Status
                        != RequirementStatus.Superseded
                    && proposal.Requirement.SupersededByRequirementId == null
                    && !dbContext.RequirementTechnicalProposals.Any(
                        newer =>
                            newer.RequirementId
                                == proposal.RequirementId
                            && (
                                newer.CreatedAtUtc
                                    > proposal.CreatedAtUtc
                                || (
                                    newer.CreatedAtUtc
                                        == proposal.CreatedAtUtc
                                    && newer.Id
                                        .CompareTo(proposal.Id) > 0
                                )
                            )))
                .OrderByDescending(proposal =>
                    proposal.CreatedAtUtc)
                .Take(5)
                .Select(proposal =>
                    new UserDashboardAttentionItemSnapshot(
                        proposal.Requirement.PreQuote.ProjectId,
                        proposal.Requirement.PreQuote.Project.Code,
                        proposal.Requirement.PreQuote.Project.Name,
                        proposal.Requirement.PreQuoteId,
                        proposal.Requirement.PreQuote.Serial,
                        proposal.Requirement.PreQuote.Name,
                        proposal.RequirementId,
                        "PROPOSAL_REVIEW",
                        "Propuesta por revisar",
                        "La propuesta técnica requiere revisión antes de continuar.",
                        proposal.CreatedAtUtc))
                .ToArrayAsync(cancellationToken);

        var processingItems = await dbContext.Requirements
            .AsNoTracking()
            .Where(requirement =>
                requirement.CreatedByUserId == userId
                && requirement.IsActive
                && requirement.Status
                    == RequirementStatus.Processing
                && requirement.SupersededByRequirementId == null)
            .OrderByDescending(requirement =>
                requirement.UpdatedAtUtc)
            .Take(5)
            .Select(requirement =>
                new UserDashboardAttentionItemSnapshot(
                    requirement.PreQuote.ProjectId,
                    requirement.PreQuote.Project.Code,
                    requirement.PreQuote.Project.Name,
                    requirement.PreQuoteId,
                    requirement.PreQuote.Serial,
                    requirement.PreQuote.Name,
                    requirement.Id,
                    "REQUIREMENT_PROCESSING",
                    "Requerimiento en análisis",
                    "El requerimiento continúa procesándose.",
                    requirement.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var attentionItems = proposalReviewItems
            .Concat(processingItems)
            .OrderByDescending(item =>
                item.UpdatedAtUtc)
            .Take(5)
            .ToArray();

        var recentProjectActivity = await dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.CreatedByUserId == userId)
            .OrderByDescending(project =>
                project.UpdatedAtUtc)
            .Take(5)
            .Select(project =>
                new UserDashboardActivityItemSnapshot(
                    project.Id,
                    project.Code,
                    project.Name,
                    null,
                    null,
                    null,
                    "PROJECT_UPDATED",
                    "Proyecto actualizado",
                    "Se registraron cambios recientes en el proyecto.",
                    project.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var recentPreQuoteActivity = await dbContext.PreQuotes
            .AsNoTracking()
            .Where(preQuote =>
                preQuote.CreatedByUserId == userId)
            .OrderByDescending(preQuote =>
                preQuote.UpdatedAtUtc)
            .Take(5)
            .Select(preQuote =>
                new UserDashboardActivityItemSnapshot(
                    preQuote.ProjectId,
                    preQuote.Project.Code,
                    preQuote.Project.Name,
                    preQuote.Id,
                    preQuote.Serial,
                    preQuote.Name,
                    "PREQUOTE_UPDATED",
                    "Precotización actualizada",
                    "Se registraron cambios recientes en la precotización.",
                    preQuote.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var recentRequirementActivity = await dbContext.Requirements
            .AsNoTracking()
            .Where(requirement =>
                requirement.CreatedByUserId == userId
                && requirement.IsActive
                && requirement.Status
                    != RequirementStatus.Cancelled
                && requirement.Status
                    != RequirementStatus.Superseded
                && requirement.SupersededByRequirementId == null)
            .OrderByDescending(requirement =>
                requirement.UpdatedAtUtc)
            .Take(5)
            .Select(requirement =>
                new UserDashboardActivityItemSnapshot(
                    requirement.PreQuote.ProjectId,
                    requirement.PreQuote.Project.Code,
                    requirement.PreQuote.Project.Name,
                    requirement.PreQuoteId,
                    requirement.PreQuote.Serial,
                    requirement.PreQuote.Name,
                    requirement.Status
                        == RequirementStatus.Processing
                        ? "REQUIREMENT_PROCESSING"
                        : requirement.Status
                            == RequirementStatus.Processed
                            ? "REQUIREMENT_PROCESSED"
                            : "REQUIREMENT_UPDATED",
                    requirement.Status
                        == RequirementStatus.Processing
                        ? "Requerimiento en análisis"
                        : requirement.Status
                            == RequirementStatus.Processed
                            ? "Requerimiento procesado"
                            : "Requerimiento actualizado",
                    requirement.Status
                        == RequirementStatus.Processing
                        ? "El requerimiento entró en procesamiento."
                        : requirement.Status
                            == RequirementStatus.Processed
                            ? "El requerimiento terminó de procesarse."
                            : "El requerimiento tuvo cambios recientes.",
                    requirement.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var recentProposalActivity =
            await dbContext.RequirementTechnicalProposals
                .AsNoTracking()
                .Where(proposal =>
                    proposal.Requirement.CreatedByUserId == userId)
                .OrderByDescending(proposal =>
                    proposal.CreatedAtUtc)
                .Take(5)
                .Select(proposal =>
                    new UserDashboardActivityItemSnapshot(
                        proposal.Requirement.PreQuote.ProjectId,
                        proposal.Requirement.PreQuote.Project.Code,
                        proposal.Requirement.PreQuote.Project.Name,
                        proposal.Requirement.PreQuoteId,
                        proposal.Requirement.PreQuote.Serial,
                        proposal.Requirement.PreQuote.Name,
                        "PROPOSAL_CREATED",
                        proposal.Status
                            == RequirementTechnicalProposalStatus.RequiresReview
                            ? "Propuesta técnica generada con revisión pendiente"
                            : "Propuesta técnica generada",
                        proposal.Status
                            == RequirementTechnicalProposalStatus.RequiresReview
                            ? "La propuesta técnica fue generada y requiere revisión."
                            : "La propuesta técnica fue generada correctamente.",
                        proposal.CreatedAtUtc))
                .ToArrayAsync(cancellationToken);

        var recentActivity = recentProjectActivity
            .Concat(recentPreQuoteActivity)
            .Concat(recentRequirementActivity)
            .Concat(recentProposalActivity)
            .OrderByDescending(item =>
                item.OccurredAtUtc)
            .Take(5)
            .ToArray();

        return new UserDashboardSnapshot(
            activeProjects,
            totalPreQuotes,
            requirementsInProgress,
            proposalsRequiringReview,
            recentProjects,
            attentionItems,
            recentActivity);
    }
}