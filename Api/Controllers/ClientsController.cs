using Application.Clients.CreateClient;
using Contracts.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients")]
public sealed class ClientsController(CreateClientService createClientService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateClientResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateClientResponse>> Create(
        [FromBody] CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createClientService.ExecuteAsync(
            new CreateClientCommand(
                request.ClientType,
                request.LegalName,
                request.TradeName,
                request.DocumentType,
                request.DocumentNumber,
                request.Email,
                request.Phone,
                request.Address,
                request.City),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var client = result.Client!;
        var response = new CreateClientResponse(
            client.Id,
            client.ClientType.ToString(),
            client.LegalName,
            client.TradeName,
            client.DocumentType?.ToString(),
            client.DocumentNumber,
            client.Email,
            client.Phone,
            client.Address,
            client.City,
            client.IsActive,
            client.CreatedAtUtc,
            client.UpdatedAtUtc);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    private ActionResult<CreateClientResponse> MapFailure(
        CreateClientFailure failure)
    {
        return failure switch
        {
            CreateClientFailure.InvalidRequest => ClientProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los datos enviados para crear el cliente no son válidos."),
            CreateClientFailure.Unauthorized => ClientProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            CreateClientFailure.InactiveUser => ClientProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para crear clientes."),
            CreateClientFailure.DuplicateDocument => ClientProblem(
                StatusCodes.Status409Conflict,
                "Cliente duplicado",
                "Ya existe un cliente con el tipo y número de documento indicados."),
            _ => ClientProblem(
                StatusCodes.Status500InternalServerError,
                "Error al crear el cliente",
                "No fue posible guardar el cliente.")
        };
    }

    private ObjectResult ClientProblem(
        int statusCode,
        string title,
        string detail)
    {
        return Problem(
            statusCode: statusCode,
            title: title,
            detail: detail);
    }
}
