using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WasteBatteriesSubmitBackend.OpenApi;

public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string SchemeId = "Bearer";
    public const string DocumentTitle = "Waste Batteries Submit API";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = DocumentTitle,
            Version = "v1",
            Description = "Manage waste batteries submissions."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Defra ID access token."
        };

        var requirement = new OpenApiSecurityRequirement();
        requirement.Add(new OpenApiSecuritySchemeReference(SchemeId, document), []);
        document.Security ??= [];
        document.Security.Add(requirement);

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security = [];
        }

        return Task.CompletedTask;
    }
}
