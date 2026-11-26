using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Config;

public class ServiceAuthConfigTests
{
    [Fact]
    public void ApplyPlatformDefaults_FillsEmptyIssuerAndOidcFromTheProcess()
    {
        var previousIssuer = Environment.GetEnvironmentVariable("CDP_JWT_ISSUER");
        var previousOidc = Environment.GetEnvironmentVariable("CDP_JWT_OIDC_URL");
        var previousJwks = Environment.GetEnvironmentVariable("CDP_JWT_JWKS_URI");
        try
        {
            Environment.SetEnvironmentVariable("CDP_JWT_ISSUER", "https://issuer.example");
            Environment.SetEnvironmentVariable("CDP_JWT_OIDC_URL", "https://issuer.example/.well-known/openid-configuration");
            Environment.SetEnvironmentVariable("CDP_JWT_JWKS_URI", "https://issuer.example/jwks");
            var config = new ServiceAuthConfig { Audience = "waste-batteries-submit-backend" };

            config.ApplyPlatformDefaults();

            Assert.Equal("https://issuer.example", config.Issuer);
            Assert.Equal("https://issuer.example/.well-known/openid-configuration", config.OidcUrl);
            Assert.Equal("https://issuer.example/jwks", config.JwksUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_JWT_ISSUER", previousIssuer);
            Environment.SetEnvironmentVariable("CDP_JWT_OIDC_URL", previousOidc);
            Environment.SetEnvironmentVariable("CDP_JWT_JWKS_URI", previousJwks);
        }
    }

    [Fact]
    public void ApplyPlatformDefaults_KeepsAConfiguredIssuer()
    {
        var config = new ServiceAuthConfig { Issuer = "https://configured.example" };

        config.ApplyPlatformDefaults();

        Assert.Equal("https://configured.example", config.Issuer);
    }

    [Fact]
    public void ValidationError_WhenAwsHasIssuerAndHttpsOidc_ReturnsNull()
    {
        var config = Aws();

        Assert.Null(config.ValidationError("Production", "dev"));
    }

    [Fact]
    public void ValidationError_WhenDevelopmentHasAToken_ReturnsNull()
    {
        var config = Development();

        Assert.Null(config.ValidationError("Development", "dev"));
    }

    [Theory]
    [InlineData("Production", "dev", "host environment is Development")]
    [InlineData("Development", "prod", "ENVIRONMENT is prod")]
    public void ValidationError_WhenDevelopmentIsNotLocal_NamesTheReason(
        string aspNetEnvironment,
        string cdpEnvironment,
        string expected)
    {
        var error = Development().ValidationError(aspNetEnvironment, cdpEnvironment);

        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationError_WhenAwsHasNoIssuer_NamesTheIssuer()
    {
        var config = Aws();
        config.Issuer = "";

        var error = config.ValidationError("Production", "dev");

        Assert.Contains("Issuer", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationError_WhenTheModeIsUnknown_NamesTheMode()
    {
        var config = Aws();
        config.Mode = "Basic";

        var error = config.ValidationError("Production", "dev");

        Assert.Contains("Mode must be Aws or Development", error, StringComparison.Ordinal);
    }

    private static ServiceAuthConfig Aws() => new()
    {
        Mode = ServiceAuthConfig.AwsMode,
        Audience = "waste-batteries-submit-backend",
        AllowedService = "waste-batteries-obligations-be",
        Issuer = "https://issuer.example",
        OidcUrl = "https://issuer.example/.well-known/openid-configuration"
    };

    private static ServiceAuthConfig Development() => new()
    {
        Mode = ServiceAuthConfig.DevelopmentMode,
        Audience = "waste-batteries-submit-backend",
        AllowedService = "waste-batteries-obligations-be",
        DevToken = "local-obligations-to-submit"
    };
}
