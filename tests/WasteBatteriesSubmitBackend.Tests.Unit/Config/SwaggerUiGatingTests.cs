using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Config;

public class SwaggerUiGatingTests
{
    [Fact]
    public void ShouldEnableSwaggerUi_WhenEnvironmentIsNotProduction_ReturnsTrue()
    {
        // Arrange
        var environment = new TestHostEnvironment(Environments.Development);
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var enabled = SwaggerUiGating.ShouldEnableSwaggerUi(environment, configuration);

        // Assert
        Assert.True(enabled);
    }

    [Fact]
    public void ShouldEnableSwaggerUi_WhenEnvironmentIsProduction_ReturnsFalse()
    {
        // Arrange
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var enabled = SwaggerUiGating.ShouldEnableSwaggerUi(environment, configuration);

        // Assert
        Assert.False(enabled);
    }

    [Fact]
    public void ShouldEnableSwaggerUi_WhenProductionAndExplicitlyEnabled_ReturnsTrue()
    {
        // Arrange
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })
            .Build();

        // Act
        var enabled = SwaggerUiGating.ShouldEnableSwaggerUi(environment, configuration);

        // Assert
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
