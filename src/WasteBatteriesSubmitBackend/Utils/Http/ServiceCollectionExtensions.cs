using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Http;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHeaderPropagationFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var traceHeader = configuration.GetValue<string>("TraceHeader");

        services.AddHeaderPropagation(options =>
        {
            if (!string.IsNullOrWhiteSpace(traceHeader))
            {
                options.Headers.Add(traceHeader);
            }
        });

        return services;
    }

    public static IServiceCollection AddProxyHttpClients(this IServiceCollection services)
    {
        services.AddTransient<ProxyHttpMessageHandler>();

        // TEMPORARY: When the first real typed client is registered, add
        // Microsoft.Extensions.Http.Resilience on that client, as waste-obligations
        // does for its downstream clients. Register it with AddHttpClientWithTracing
        // or AddHttpClientWithTracingAndProxy. Do not add resilience before a client exists.

        return services;
    }
}
