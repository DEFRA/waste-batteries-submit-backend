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
    // Minimal APIs only throw on a bad body or query in Development by default. Elsewhere they write an
    // empty 400 and log at Debug. Throwing everywhere sends it through ExceptionLoggingHandler as problem details.
    services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
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
    services.AddPomSubmissionResponseCompression();
    services.AddPomSubmissionServices(configuration);
    services.AddExampleServices();
    services.AddExampleDataServices();
}

[ExcludeFromCodeCoverage]
static void ConfigureMiddleware(WebApplication app)
{
    // Outside the exception handler, so the request log has the status the handler wrote.
    // Inside it, every handled exception (including a validation 400) is logged as an error 500.
    app.UseSerilogRequestLogging();
    // Before the rest, so an exception from any later middleware becomes problem details.
    // Unexpected exceptions are logged as errors. Validation failures are logged
    // as information and returned as a validation problem.
    app.UseExceptionHandler();
    app.UsePomSubmissionResponseCompression();
    // TEMPORARY: Call UseStatusCodePages() immediately after UseExceptionHandler, as the
    // EPR backend does, so a status with no body (such as an unknown route) is written
    // as problem details. Check that /health, Swagger UI, and the JWT challenge still
    // return their own responses, because status-code pages only fill an empty body.
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
