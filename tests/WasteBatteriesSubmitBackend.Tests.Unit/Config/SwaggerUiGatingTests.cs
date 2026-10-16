using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Config;

public class SwaggerUiGatingTests
{
    [Fact]
    public void Swagger_ui_is_enabled_outside_production()
    {
        var enabled = SwaggerUiGating.ShouldEnableSwaggerUi(
            new TestHostEnvironment(Environments.Development),
            new ConfigurationBuilder().Build());

        Assert.True(enabled);
    }

    [Fact]
    public void Swagger_ui_is_disabled_in_production_unless_explicitly_enabled()
    {
        var environment = new TestHostEnvironment(Environments.Production);

        var disabled = SwaggerUiGating.ShouldEnableSwaggerUi(environment, new ConfigurationBuilder().Build());
        var enabled = SwaggerUiGating.ShouldEnableSwaggerUi(
            environment,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })
                .Build());

        Assert.False(disabled);
        Assert.True(enabled);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "WasteBatteriesSubmitBackend.Tests.Unit";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
