using Microsoft.AspNetCore.Http;

namespace Api.Controllers;

public sealed class HistoricalDocumentEstimateForm
{
    public List<IFormFile> Files { get; init; } = [];
    public Guid? ProjectId { get; init; }
    public Guid? RequirementId { get; init; }
}
