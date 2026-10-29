using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.OpenApi;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        // TEMPORARY: Generate the OpenAPI document at build time, as waste-obligations does
        // with OpenApiGenerateDocuments. Generation starts the app, and startup currently
        // requires JWT and Mongo. Skip that infrastructure when the entry assembly is
        // GetDocument.Insider, the same guard as IsProjectBuildGeneratingOpenApi.
        // Keep Swagger UI at /swagger. Do not switch the explorer to ReDoc.
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
        });
        return services;
    }
}
