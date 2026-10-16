using System.Diagnostics.CodeAnalysis;
using Swashbuckle.AspNetCore.SwaggerUI;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.OpenApi;

[ExcludeFromCodeCoverage]
public static class WebApplicationExtensions
{
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

        if (SwaggerUiGating.ShouldEnableSwaggerUi(app.Environment, app.Configuration))
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", BearerSecuritySchemeTransformer.DocumentTitle);
                options.RoutePrefix = "swagger";
            });

            app.MapGet("/", () => Results.Redirect("/swagger"))
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        return app;
    }
}
