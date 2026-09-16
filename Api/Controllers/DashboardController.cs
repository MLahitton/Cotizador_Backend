using Application.Dashboard.GetUserDashboard;
using Contracts.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public sealed class DashboardController(
    GetUserDashboardService getUserDashboardService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserDashboardResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDashboardResponse>> Get(
        CancellationToken cancellationToken)
    {
        var result =
            await getUserDashboardService.ExecuteAsync(
                cancellationToken);

        if (result.Failure
            == GetUserDashboardFailure.Unauthorized)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail:
                    "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure
            == GetUserDashboardFailure.InactiveUser)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail:
                    "El usuario no tiene acceso a la aplicacion.");
        }

        if (result.Failure
            == GetUserDashboardFailure.Forbidden)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail:
                    "Este panel esta disponible para usuarios operativos.");
        }

        return Ok(
            new UserDashboardResponse(
                result.ActiveProjects,
                result.TotalPreQuotes,
                result.RequirementsInProgress,
                result.ProposalsRequiringReview,
                result.RecentProjects
                    .Select(project =>
                        new UserDashboardRecentProjectResponse(
                            project.ProjectId,
                            project.Code,
                            project.Name,
                            project.ClientId,
                            project.ClientName,
                            project.IsActive,
                            project.UpdatedAtUtc))
                    .ToArray(),
                result.AttentionItems
                    .Select(item =>
                        new UserDashboardAttentionItemResponse(
                            item.ProjectId,
                            item.ProjectCode,
                            item.ProjectName,
                            item.PreQuoteId,
                            item.PreQuoteSerial,
                            item.PreQuoteName,
                            item.RequirementId,
                            item.Type,
                            item.Title,
                            item.Description,
                            item.UpdatedAtUtc))
                    .ToArray(),
                result.RecentActivity
                    .Select(item =>
                        new UserDashboardActivityItemResponse(
                            item.ProjectId,
                            item.ProjectCode,
                            item.ProjectName,
                            item.PreQuoteId,
                            item.PreQuoteSerial,
                            item.PreQuoteName,
                            item.Type,
                            item.Title,
                            item.Description,
                            item.OccurredAtUtc))
                    .ToArray()));
    }
}