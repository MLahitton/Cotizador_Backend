using Application.Catalogs.GetGlassTypesCatalog;
using Contracts.Catalogs;
using Domain.Catalogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/catalogs/glass-types")]
public sealed class GlassTypesCatalogController(
    GetGlassTypesCatalogService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetGlassTypesCatalogResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                GetGlassTypesCatalogFailure.Unauthorized => Problem(
                    statusCode: 401,
                    title: "No autorizado",
                    detail: "No fue posible identificar al usuario autenticado."),
                GetGlassTypesCatalogFailure.InactiveUser => Problem(
                    statusCode: 403,
                    title: "Usuario inactivo",
                    detail: "El usuario no tiene acceso al catalogo de vidrios."),
                _ => Problem(
                    statusCode: 500,
                    title: "Error al consultar el catalogo",
                    detail: "No fue posible consultar el catalogo de vidrios.")
            };
        }

        return Ok(new GetGlassTypesCatalogResponse(
            result.Items.Select(item => new GlassTypeCatalogItemResponse(
                item.GlassTypeId,
                item.Code,
                item.Name,
                item.Description,
                item.IsActive,
                Map(item.CurrentPriceRange!)))
                .ToArray()));
    }

    private static GlassPriceRangeResponse Map(
        Application.Common.Abstractions.Catalogs
            .GlassPriceRangeCatalogReadModel value) =>
        new(
            value.GlassPriceRangeVersionId,
            value.Version,
            value.MinimumPricePerSquareMeter,
            value.MaximumPricePerSquareMeter,
            value.Currency,
            value.Status switch
            {
                GlassPriceRangeStatus.Preliminary => "PRELIMINARY",
                GlassPriceRangeStatus.Active => "ACTIVE",
                _ => "RETIRED"
            },
            value.ValidFromUtc,
            value.ValidToUtc);
}
