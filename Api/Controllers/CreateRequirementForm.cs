using Microsoft.AspNetCore.Http;

namespace Api.Controllers;

public sealed class CreateRequirementForm
{
    public List<IFormFile> Files { get; init; } = [];
}
