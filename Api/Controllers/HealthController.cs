using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        var response = new HealthResponse(
            Status: "Healthy",
            Service: "Cotizador Backend",
            TimestampUtc: DateTimeOffset.UtcNow);

        return Ok(response);
    }
}

public sealed record HealthResponse(
    string Status,
    string Service,
    DateTimeOffset TimestampUtc);