using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/historical-pricing")]
public sealed class HistoricalPricingController(
    IHistoricalTechnicalPriceEstimator estimator,
    IHistoricalQuoteCorpus corpus) : ControllerBase
{
    [HttpPost("technical-estimate")]
    [ProducesResponseType<HistoricalTechnicalPriceEstimateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<HistoricalTechnicalPriceEstimateResponse>> Estimate(
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

        var corpusSnapshot = corpus.Current;
        if (!corpusSnapshot.IsAvailable)
        {
            corpusSnapshot = await corpus.ReloadAsync(cancellationToken);
        }
        if (!corpusSnapshot.IsAvailable)
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
            var query = new HistoricalCandidateQuery(
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
                GlassComposition: request.GlassComposition);
            var result = await estimator.EstimateAsync(query, cancellationToken);
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
                title: "Error al estimar precio tecnico",
                detail: "No fue posible calcular el rango tecnico historico.");
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

    private static HistoricalTechnicalPriceEstimateResponse Map(
        HistoricalTechnicalPriceEstimate result) =>
        new(
            result.Expected is null ? "NOT_PRICEABLE" : "ESTIMATED",
            result.Currency,
            result.PricingSource,
            result.Minimum,
            result.Expected,
            result.Maximum,
            result.ConfidenceScore,
            result.ConfidenceLevel.ToString().ToUpperInvariant(),
            result.CandidateCount,
            result.SimilarityEvaluatedCount,
            result.StrongComparableCount,
            result.RequiresReview,
            result.Assumptions,
            result.MissingData,
            result.Comparables.Select(value =>
                new HistoricalTechnicalPriceComparableResponse(
                    value.CandidateId,
                    value.HistoricalReference,
                    value.BackendTechnicalScore,
                    value.Ai2SimilarityScore,
                    value.SimilarityLevel,
                    value.FinalWeight,
                    value.PublicUnitPrice,
                    value.HistoricalUnitArea,
                    value.ProjectedPrice,
                    value.MatchingTier,
                    value.MatchedSystem,
                    value.MatchedGlass,
                    value.MatchedFinish,
                    value.MatchedCommercialLine,
                    value.FallbackReasons ?? [])).ToArray());
}
