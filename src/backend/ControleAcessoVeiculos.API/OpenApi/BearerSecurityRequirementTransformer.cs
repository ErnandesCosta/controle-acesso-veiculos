using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControleAcessoVeiculos.API.OpenApi;

internal sealed class BearerSecurityRequirementTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var allowsAnonymousAccess = context.Description
            .ActionDescriptor
            .EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();
        if (allowsAnonymousAccess)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
