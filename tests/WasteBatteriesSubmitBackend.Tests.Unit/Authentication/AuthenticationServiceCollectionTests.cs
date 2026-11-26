using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WasteBatteriesSubmitBackend.Authentication;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Authentication;

public class AuthenticationServiceCollectionTests
{
    [Fact]
    public void AddAuthenticationAuthorization_ReloadsSigningKeysWhenTheIssuerRotatesThem()
    {
        var services = new ServiceCollection();
        services.AddAuthenticationAuthorization(Config(
            ("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration"),
            ("Jwt:Audience", "test-audience"),
            ("Jwt:UserIdClaim", "sub")), Host());
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var configurationManager = Assert.IsType<BaseConfigurationManager>(options.ConfigurationManager, exactMatch: false);

        Assert.True(AppContext.TryGetSwitch("Switch.Microsoft.IdentityModel.UpdateConfigAsBlocking", out var blocking));
        Assert.True(blocking);
        Assert.True(options.RefreshOnIssuerKeyNotFound);
        Assert.Equal(TimeSpan.FromMinutes(5), options.AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), configurationManager.AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), configurationManager.RefreshInterval);
    }

    [Fact]
    public void AddAuthenticationAuthorization_UsesTheConfiguredSigningKeyRefreshIntervals()
    {
        var services = new ServiceCollection();
        services.AddAuthenticationAuthorization(Config(
            ("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration"),
            ("Jwt:Audience", "test-audience"),
            ("Jwt:UserIdClaim", "sub"),
            ("Jwt:AutomaticRefreshInterval", "00:10:00"),
            ("Jwt:RefreshInterval", "00:00:30")), Host());
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var configurationManager = Assert.IsType<BaseConfigurationManager>(options.ConfigurationManager, exactMatch: false);

        Assert.Equal(TimeSpan.FromMinutes(10), options.AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), configurationManager.AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), configurationManager.RefreshInterval);
    }

    [Fact]
    public void AddAuthenticationAuthorization_RejectsASigningKeyRefreshShorterThanTheLibraryAllows()
    {
        var services = new ServiceCollection();
        services.AddAuthenticationAuthorization(Config(
            ("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration"),
            ("Jwt:Audience", "test-audience"),
            ("Jwt:UserIdClaim", "sub"),
            ("Jwt:AutomaticRefreshInterval", "00:01:00")), Host());
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<JwtConfig>>().Value);

        Assert.Contains("Jwt:AutomaticRefreshInterval", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAuthenticationAuthorization_RejectsDevelopmentModeOutsideDevelopment()
    {
        var services = new ServiceCollection();
        services.AddAuthenticationAuthorization(
            Config(
                ("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration"),
                ("Jwt:Audience", "test-audience"),
                ("Jwt:UserIdClaim", "sub"),
                ("ServiceAuth:Mode", "Development"),
                ("ServiceAuth:Audience", "waste-batteries-submit-backend"),
                ("ServiceAuth:AllowedService", "waste-batteries-obligations-be"),
                ("ServiceAuth:DevToken", "local-token")),
            Host("Production"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<ServiceAuthConfig>>().Value);

        Assert.Contains("host environment is Development", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

    private static IHostEnvironment Host(string name = "Development") => new TestEnvironment(name);

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "WasteBatteriesSubmitBackend.Tests.Unit";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
