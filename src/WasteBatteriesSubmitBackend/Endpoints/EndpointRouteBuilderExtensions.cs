using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Authentication;
using WasteBatteriesSubmitBackend.Example.Endpoints;
using WasteBatteriesSubmitBackend.ExampleData.Endpoints;
using WasteBatteriesSubmitBackend.PomSubmission.Endpoints;

namespace WasteBatteriesSubmitBackend.Endpoints;

[ExcludeFromCodeCoverage]
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPomSubmissionEndpoints().RequireAuthorization(ServiceAuthDefaults.Policy);

        // TEMPORARY: Remove before deploying
        app.MapExampleEndpoints().RequireAuthorization();
        app.MapExampleDataEndpoints().RequireAuthorization();

        return app;
    }
}
