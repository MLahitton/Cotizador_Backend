using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/historical-pricing")]
public sealed class HistoricalCommercialPricingController(
    IHistoricalCommercialPriceEstimator estimator,
    IHistoricalQuoteCorpus corpus) : ControllerBase
{
    [HttpPost("commercial-estimate")]
    [ProducesResponseType<HistoricalCommercialPriceEstimateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<HistoricalCommercialPriceEstimateResponse>> Estimate(
        [FromBody] HistoricalTechnicalPriceEstimateRequest request,
        CancellationToken cancellationToken)
    {
        if (IsInvalid(request))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Elemento tecnico invalido",
                Detail = "Los datos tecnicos requeridos deben ser validos y no vacios."
            });
        }

        var snapshot = corpus.Current;
        if (!snapshot.IsAvailable)
        {
            snapshot = await corpus.ReloadAsync(cancellationToken);
        }
        if (!snapshot.IsAvailable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Corpus historico no disponible",
                Detail = "No fue posible cargar el corpus historico configurado."
            });
        }

        try
        {
            var result = await estimator.EstimateAsync(
                new HistoricalCandidateQuery(
                    request.Category,
                    request.System,
                    request.GlassFamily,
                    request.GlassThickness,
                    request.Configuration,
                    request.WidthMm,
                    request.HeightMm,
                    request.AreaM2,
                    request.Finish,
                    request.Quantity,
                    ExcludedCandidateIds: request.ExcludeCandidateIds,
                    ExcludedQuoteIds: request.ExcludeQuoteIds,
                    GlassComposition: request.GlassComposition),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al estimar precio comercial",
                detail: "No fue posible calcular el rango comercial historico.");
        }
    }

    private static bool IsInvalid(HistoricalTechnicalPriceEstimateRequest request) =>
        string.IsNullOrWhiteSpace(request.Category)
        || string.IsNullOrWhiteSpace(request.System)
        || string.IsNullOrWhiteSpace(request.GlassFamily)
        || string.IsNullOrWhiteSpace(request.Configuration)
        || request.GlassThickness <= 0
        || request.AreaM2 <= 0
        || request.Quantity <= 0
        || request.WidthMm is <= 0
        || request.HeightMm is <= 0;

    private static HistoricalCommercialPriceEstimateResponse Map(
        HistoricalCommercialPriceEstimate value) =>
        new(
            value.FinalExpected is null ? "NOT_PRICEABLE" : "ESTIMATED",
            value.Currency,
            value.PricingSource,
            PricingBasis(value.PricingBasis),
            value.TechnicalMinimum,
            value.TechnicalExpected,
            value.TechnicalMaximum,
            value.AdministrationMinimum,
            value.AdministrationExpected,
            value.AdministrationMaximum,
            value.ContingencyMinimum,
            value.ContingencyExpected,
            value.ContingencyMaximum,
            value.ProfitMinimum,
            value.ProfitExpected,
            value.ProfitMaximum,
            value.VatOnProfitMinimum,
            value.VatOnProfitExpected,
            value.VatOnProfitMaximum,
            value.FinalMinimum,
            value.FinalExpected,
            value.FinalMaximum,
            value.ConfidenceScore,
            value.ConfidenceLevel.ToString().ToUpperInvariant(),
            value.RequiresReview,
            value.Assumptions,
            value.MissingData);

    private static string PricingBasis(HistoricalPricingBasis value) => value switch
    {
        HistoricalPricingBasis.PublicQuotedItemPrices => "PUBLIC_QUOTED_ITEM_PRICES",
        HistoricalPricingBasis.InternalCostBasis => "INTERNAL_COST_BASIS",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
