using Api.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.OpenApi;

public sealed class FpProPreviewMultipartOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string MultipartFormData = "multipart/form-data";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!IsFpProPreviewOperation(context))
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [MultipartFormData] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "file" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "binary"
                            }
                        }
                    }
                }
            }
        };

        return Task.CompletedTask;
    }

    private static bool IsFpProPreviewOperation(
        OpenApiOperationTransformerContext context) =>
        context.Description.ActionDescriptor.RouteValues
                .TryGetValue("controller", out var controller)
            && string.Equals(
                controller,
                "FpProProposals",
                StringComparison.Ordinal)
            && context.Description.ActionDescriptor.RouteValues
                .TryGetValue("action", out var action)
            && string.Equals(
                action,
                nameof(FpProProposalsController.Preview),
                StringComparison.Ordinal);
}
