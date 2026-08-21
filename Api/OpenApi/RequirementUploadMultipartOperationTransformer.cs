using Api.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.OpenApi;

public sealed class RequirementUploadMultipartOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string MultipartFormData = "multipart/form-data";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!IsRequirementUploadOperation(context))
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
                        Required = new HashSet<string>
                        {
                            "files"
                        },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["files"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Array,
                                Items = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.String,
                                    Format = "binary"
                                }
                            }
                        }
                    }
                }
            }
        };

        return Task.CompletedTask;
    }

    private static bool IsRequirementUploadOperation(
        OpenApiOperationTransformerContext context)
    {
        return context.Description.ActionDescriptor.RouteValues
                .TryGetValue("controller", out var controller)
            && string.Equals(
                controller,
                "PreQuoteRequirements",
                StringComparison.Ordinal)
            && context.Description.ActionDescriptor.RouteValues
                .TryGetValue("action", out var action)
            && string.Equals(
                action,
                nameof(PreQuoteRequirementsController.Create),
                StringComparison.Ordinal);
    }
}
