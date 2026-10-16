using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Example.Endpoints;
using WasteBatteriesSubmitBackend.ExampleData.Endpoints;

namespace WasteBatteriesSubmitBackend.Endpoints;

[ExcludeFromCodeCoverage]
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Remove before deploying
        app.MapExampleEndpoints().RequireAuthorization();
        app.MapExampleDataEndpoints().RequireAuthorization();

        return app;
    }
}
