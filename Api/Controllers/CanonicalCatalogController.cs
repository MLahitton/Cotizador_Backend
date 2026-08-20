using Application.Catalogs.GetCanonicalCatalog;
using Contracts.Catalogs;
using Domain.Catalogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/catalogs/canonical")]
public sealed class CanonicalCatalogController(
    GetCanonicalCatalogService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetCanonicalCatalogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                GetCanonicalCatalogFailure.Unauthorized => Problem(
                    statusCode: 401,
                    title: "No autorizado",
                    detail: "No fue posible identificar al usuario autenticado."),
                GetCanonicalCatalogFailure.InactiveUser => Problem(
                    statusCode: 403,
                    title: "Usuario inactivo",
                    detail: "El usuario no tiene acceso al catalogo canonico."),
                _ => Problem(
                    statusCode: 500,
                    title: "Error al consultar el catalogo",
                    detail: "No fue posible consultar el catalogo canonico.")
            };
        }

        return Ok(new GetCanonicalCatalogResponse(
            result.Systems.Select(value => new CanonicalCatalogSystemResponse(
                value.Code,
                value.Name,
                value.TechnicalName,
                value.CommercialName,
                value.FunctionalType,
                value.Family,
                value.Series,
                value.CommercialLine,
                value.Variant,
                value.IsSelectable,
                value.ActiveForRecognition,
                value.Priceable,
                value.FuturePriceable,
                value.RequiresReview,
                value.IsActive)).ToArray(),
            result.Frames.Select(value => new CanonicalCatalogFrameResponse(
                value.Code,
                value.Name,
                value.IsActive)).ToArray(),
            result.Finishes.Select(value => new CanonicalCatalogFinishResponse(
                value.Code,
                value.Name,
                value.RequiresReview,
                value.IsActive)).ToArray(),
            result.Aliases.Select(value => new CanonicalCatalogAliasResponse(
                Map(value.Category),
                value.Alias,
                value.NormalizedAlias,
                value.CanonicalCode,
                Map(value.MatchPolicy),
                value.RequiresContext,
                value.Confidence,
                value.IsActive)).ToArray()));
    }

    private static string Map(CatalogAliasCategory value) => value switch
    {
        CatalogAliasCategory.System => "SYSTEM",
        CatalogAliasCategory.Frame => "FRAME",
        CatalogAliasCategory.Finish => "FINISH",
        _ => throw new InvalidOperationException()
    };

    private static string Map(CatalogAliasMatchPolicy value) => value switch
    {
        CatalogAliasMatchPolicy.ExactNormalized => "EXACT_NORMALIZED",
        CatalogAliasMatchPolicy.TechnicalPhrase => "TECHNICAL_PHRASE",
        _ => throw new InvalidOperationException()
    };
}
