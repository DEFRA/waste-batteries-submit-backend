using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Authentication;
using WasteBatteriesSubmitBackend.Endpoints;
using WasteBatteriesSubmitBackend.Example;
using WasteBatteriesSubmitBackend.ExampleData;
using WasteBatteriesSubmitBackend.PomSubmission;
using WasteBatteriesSubmitBackend.OpenApi;
using WasteBatteriesSubmitBackend.Utils;
using WasteBatteriesSubmitBackend.Utils.Health;
using WasteBatteriesSubmitBackend.Utils.Http;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Time;
using Serilog;

var app = BuildApp(args);
await app.RunAsync();

[ExcludeFromCodeCoverage]
static WebApplication BuildApp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureHost(builder);
    ConfigureServices(builder);

    var app = builder.Build();

    ConfigureMiddleware(app);
    ConfigureEndpoints(app);

    return app;
}

[ExcludeFromCodeCoverage]
static void ConfigureHost(WebApplicationBuilder builder)
{
    builder.Host.UseSerilog(CdpLogging.Configuration);
}

[ExcludeFromCodeCoverage]
static void ConfigureServices(WebApplicationBuilder builder)
{
    var services = builder.Services;
    var configuration = builder.Configuration;

    // Trust material must be loaded before anything creates outbound connections.
    services.LoadCustomTrustStoreFromEnvironment();

    services.AddExceptionHandler<ExceptionLoggingHandler>();
    services.AddProblemDetails();
    services.AddValidation();
    services.AddHttpContextAccessor();

    services.AddAuthenticationAuthorization(configuration);
    services.AddHeaderPropagationFromConfiguration(configuration);
    services.AddProxyHttpClients();
    services.AddMongo(configuration);
    services.AddHealth();
    services.AddApiOpenApi();

    services.AddTimeProvider();
    services.AddPomSubmissionServices(configuration);
    services.AddExampleServices();
    services.AddExampleDataServices();
}

[ExcludeFromCodeCoverage]
static void ConfigureMiddleware(WebApplication app)
{
    // First, so an exception from any later middleware becomes problem details.
    // Unexpected exceptions are logged as errors. Validation failures are logged
    // as information and returned as a validation problem.
    app.UseExceptionHandler();
    // TODO: Call UseStatusCodePages() immediately after UseExceptionHandler, as the
    // EPR backend does, so a status with no body (such as an unknown route) is written
    // as problem details. Check that /health, Swagger UI, and the JWT challenge still
    // return their own responses, because status-code pages only fill an empty body.
    app.UseSerilogRequestLogging();
    app.UseHeaderPropagation();
    app.UseAuthentication();
    app.UseAuthorization();
}

[ExcludeFromCodeCoverage]
static void ConfigureEndpoints(WebApplication app)
{
    app.MapHealth();
    app.MapApiDocumentation();
    app.MapApiEndpoints();
}
