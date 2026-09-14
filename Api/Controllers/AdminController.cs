using Api.Authorization;
using Application.Administration.GetAdminDashboard;
using Application.Administration.GetAdminPreQuotes;
using Application.Administration.GetAdminUsers;
using Application.Common.Abstractions.Authentication;
using Contracts.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminController(
    ICurrentUser currentUser,
    GetAdminDashboardService getAdminDashboardService,
    GetAdminUsersService getAdminUsersService,
    GetAdminPreQuotesService getAdminPreQuotesService)
    : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    public IActionResult GetAdminIdentity()
    {
        return Ok(new
        {
            userId = currentUser.UserId,
            role = currentUser.Role?
                .ToString()
                .ToUpperInvariant(),
            isAdmin = currentUser.IsAdmin
        });
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<AdminDashboardResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var result = await getAdminDashboardService.ExecuteAsync(
            cancellationToken);

        if (result.Failure == GetAdminDashboardFailure.Unauthorized)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail:
                    "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure == GetAdminDashboardFailure.InactiveUser)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail:
                    "El usuario no tiene acceso a la aplicacion.");
        }

        if (result.Failure == GetAdminDashboardFailure.Forbidden)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail:
                    "El usuario no tiene permisos administrativos.");
        }

        return Ok(new AdminDashboardResponse(
            result.TotalUsers,
            result.ActiveUsers,
            result.UsersActiveToday,
            result.UsersActiveLast7Days,
            result.UsersActiveLast30Days,
            result.TotalPreQuotes,
            result.PreQuotesToday,
            result.PreQuotesThisWeek,
            result.PreQuotesThisMonth));
    }

    [HttpGet("users")]
    [ProducesResponseType<GetAdminUsersResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetAdminUsersResponse>> GetUsers(
        [FromQuery] string? search,
        [FromQuery] string? status = "all",
        [FromQuery] string? role = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getAdminUsersService.ExecuteAsync(
            new GetAdminUsersQuery(
                search,
                status,
                role,
                page,
                pageSize),
            cancellationToken);

        if (result.Failure == GetAdminUsersFailure.InvalidRequest)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud invalida",
                detail:
                    "Los filtros de consulta no son validos.");
        }

        if (result.Failure == GetAdminUsersFailure.Unauthorized)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail:
                    "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure == GetAdminUsersFailure.InactiveUser)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail:
                    "El usuario no tiene acceso a la aplicacion.");
        }

        if (result.Failure == GetAdminUsersFailure.Forbidden)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail:
                    "El usuario no tiene permisos administrativos.");
        }

        if (result.Failure == GetAdminUsersFailure.QueryError)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de consulta",
                detail:
                    "No fue posible consultar los usuarios.");
        }

        var usersPage = result.Page!;

        return Ok(new GetAdminUsersResponse(
            usersPage.Items
                .Select(user => new AdminUserListItemResponse(
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.ProfilePictureUrl,
                    user.IsActive,
                    user.Role
                        .ToString()
                        .ToUpperInvariant(),
                    user.LastLoginAtUtc,
                    user.CreatedAtUtc,
                    user.UpdatedAtUtc,
                    user.PreQuoteCount))
                .ToArray(),
            usersPage.Page,
            usersPage.PageSize,
            usersPage.TotalCount,
            usersPage.TotalPages));
    }

    [HttpGet("prequotes")]
    [ProducesResponseType<GetAdminPreQuotesResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetAdminPreQuotesResponse>> GetPreQuotes(
        [FromQuery] string? search,
        [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getAdminPreQuotesService.ExecuteAsync(
            new GetAdminPreQuotesQuery(
                search,
                userId,
                fromUtc,
                toUtc,
                page,
                pageSize),
            cancellationToken);

        if (result.Failure ==
            GetAdminPreQuotesFailure.InvalidRequest)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud invalida",
                detail:
                    "Los filtros de consulta no son validos.");
        }

        if (result.Failure ==
            GetAdminPreQuotesFailure.Unauthorized)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail:
                    "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure ==
            GetAdminPreQuotesFailure.InactiveUser)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail:
                    "El usuario no tiene acceso a la aplicacion.");
        }

        if (result.Failure ==
            GetAdminPreQuotesFailure.Forbidden)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail:
                    "El usuario no tiene permisos administrativos.");
        }

        if (result.Failure ==
            GetAdminPreQuotesFailure.QueryError)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de consulta",
                detail:
                    "No fue posible consultar las precotizaciones.");
        }

        var preQuotesPage = result.Page!;

        return Ok(new GetAdminPreQuotesResponse(
            preQuotesPage.Items
                .Select(preQuote => new AdminPreQuoteListItemResponse(
                    preQuote.Id,
                    preQuote.ProjectId,
                    preQuote.Serial,
                    preQuote.Name,
                    preQuote.DocumentCount,
                    preQuote.CreatedAtUtc,
                    preQuote.UpdatedAtUtc,
                    new AdminPreQuoteUserResponse(
                        preQuote.CreatedBy.Id,
                        preQuote.CreatedBy.Email,
                        preQuote.CreatedBy.FirstName,
                        preQuote.CreatedBy.LastName),
                    new AdminPreQuoteProjectResponse(
                        preQuote.Project.Id,
                        preQuote.Project.Code,
                        preQuote.Project.Name),
                    preQuote.HasRequirement,
                    preQuote.LatestRequirementId,
                    preQuote.LatestRequirementStatus,
                    preQuote.HasTechnicalProposal,
                    preQuote.TechnicalProposalId,
                    preQuote.TechnicalProposalItemCount,
                    preQuote.LatestAttemptState,
                    preQuote.LatestAttemptOutcome,
                    preQuote.LatestAttemptErrorCode))
                .ToArray(),
            preQuotesPage.Page,
            preQuotesPage.PageSize,
            preQuotesPage.TotalCount,
            preQuotesPage.TotalPages));
    }
}