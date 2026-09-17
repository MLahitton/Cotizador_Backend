using Application.Search.GetGlobalSearch;
using Contracts.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/search")]
public sealed class SearchController(
    GetGlobalSearchService getGlobalSearchService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GlobalSearchResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GlobalSearchResponse>> Get(
        [FromQuery(Name = "q")] string? search,
        CancellationToken cancellationToken)
    {
        var result =
            await getGlobalSearchService.ExecuteAsync(
                search,
                cancellationToken);

        if (result.Failure
            == GetGlobalSearchFailure.InvalidRequest)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status400BadRequest,
                title: "Búsqueda inválida",
                detail:
                    "La búsqueda debe tener entre 2 y 100 caracteres.");
        }

        if (result.Failure
            == GetGlobalSearchFailure.Unauthorized)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail:
                    "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure
            == GetGlobalSearchFailure.InactiveUser)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail:
                    "El usuario no tiene acceso a la aplicación.");
        }

        if (result.Failure
            == GetGlobalSearchFailure.Forbidden)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status403Forbidden,
                title: "Acceso denegado",
                detail:
                    "El usuario no tiene permisos para realizar búsquedas.");
        }

        var searchResult = result.Search!;

        return Ok(
            new GlobalSearchResponse(
                searchResult.Projects
                    .Select(project =>
                        new GlobalSearchProjectResponse(
                            project.ProjectId,
                            project.Code,
                            project.Name,
                            project.ClientName))
                    .ToArray(),
                searchResult.PreQuotes
                    .Select(preQuote =>
                        new GlobalSearchPreQuoteResponse(
                            preQuote.PreQuoteId,
                            preQuote.Serial,
                            preQuote.Name,
                            preQuote.ProjectId,
                            preQuote.ProjectCode,
                            preQuote.ProjectName))
                    .ToArray(),
                searchResult.Clients
                    .Select(client =>
                        new GlobalSearchClientResponse(
                            client.ClientId,
                            client.Name,
                            client.DocumentType,
                            client.DocumentNumber))
                    .ToArray()));
    }
}