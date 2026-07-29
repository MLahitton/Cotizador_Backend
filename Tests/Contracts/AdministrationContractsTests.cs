using System.Text.Json;
using Contracts.Projects;
using Xunit;

namespace CotizadorBackend.Tests.Contracts;

public sealed class AdministrationContractsTests
{
    [Fact]
    public void GetProjectsResponse_SerializesApprovedCamelCaseShape()
    {
        var response = new GetProjectsResponse(
            [
                new AdministrativeProjectListItemResponse(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "PR-001",
                    "Proyecto",
                    null,
                    "Bogota",
                    true,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    new ProjectClientSummaryResponse(
                        Guid.NewGuid(),
                        "Company",
                        "Cliente",
                        null,
                        "Nit",
                        "9001234567"))
            ],
            1,
            20,
            1,
            1);

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"items\"", json);
        Assert.Contains("\"pageSize\"", json);
        Assert.Contains("\"totalCount\"", json);
        Assert.Contains("\"totalPages\"", json);
        Assert.Contains("\"client\"", json);
        Assert.DoesNotContain("createdByUserId", json);
        Assert.DoesNotContain("updatedByUserId", json);
        Assert.DoesNotContain("statusChangedByUserId", json);
        Assert.DoesNotContain("documents", json);
        Assert.DoesNotContain("storage", json);
    }
}
