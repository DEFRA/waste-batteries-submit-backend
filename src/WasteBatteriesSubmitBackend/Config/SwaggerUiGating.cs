namespace WasteBatteriesSubmitBackend.Config;

/// <summary>
/// Decides whether the Swagger UI explorer is added to the pipeline.
/// </summary>
internal static class SwaggerUiGating
{
    /// <summary>
    /// Swagger UI is enabled outside Production, or in any environment that
    /// sets <c>Swagger:Enabled</c> to <c>true</c>.
    /// </summary>
    internal static bool ShouldEnableSwaggerUi(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return !environment.IsProduction() || configuration.GetValue<bool>("Swagger:Enabled");
    }
}
