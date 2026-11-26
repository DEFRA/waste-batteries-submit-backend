using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Utils.Http;

namespace WasteBatteriesSubmitBackend.Authentication;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    // Makes RequestRefresh mark the cache stale, so the validation retry waits for the new keys.
    // The default refresh returns the cached keys and continues on a background thread, so the
    // request that noticed the new kid is still rejected. The wait between reads is config.
    private const string UpdateConfigAsBlockingSwitch = "Switch.Microsoft.IdentityModel.UpdateConfigAsBlocking";

    public static IServiceCollection AddAuthenticationAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // The handler caches the issuer's signing keys. On a kid mismatch it re-reads them and
        // checks this request again. Jwt:AutomaticRefreshInterval and Jwt:RefreshInterval set the cadence.
        AppContext.SetSwitch(UpdateConfigAsBlockingSwitch, true);

        var jwtConfig = configuration.GetRequiredSection("Jwt").Get<JwtConfig>()
                        ?? throw new InvalidOperationException("Jwt configuration is required.");

        services
            .AddOptions<JwtConfig>()
            .Bind(configuration.GetRequiredSection("Jwt"))
            .ValidateDataAnnotations()
            .Validate(config => !string.IsNullOrWhiteSpace(config.MetadataAddress), "Jwt:MetadataAddress is required.")
            .Validate(config => !string.IsNullOrWhiteSpace(config.Audience), "Jwt:Audience is required.")
            .Validate(
                config => config.AutomaticRefreshInterval >= JwtConfig.MinimumAutomaticRefreshInterval,
                $"Jwt:AutomaticRefreshInterval must be at least {JwtConfig.MinimumAutomaticRefreshInterval:c}.")
            .Validate(
                config => config.RefreshInterval >= JwtConfig.MinimumRefreshInterval,
                $"Jwt:RefreshInterval must be at least {JwtConfig.MinimumRefreshInterval:c}.")
            .ValidateOnStart();

        var authentication = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = jwtConfig.MetadataAddress;
                options.Audience = jwtConfig.Audience;
                options.RequireHttpsMetadata = jwtConfig.RequireHttpsMetadata;
                options.MapInboundClaims = false;
                options.RefreshOnIssuerKeyNotFound = true;
                options.AutomaticRefreshInterval = jwtConfig.AutomaticRefreshInterval;
                options.RefreshInterval = jwtConfig.RefreshInterval;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    NameClaimType = jwtConfig.UserIdClaim
                };
            });

        var serviceAuthSection = configuration.GetSection(ServiceAuthConfig.SectionName);
        if (!serviceAuthSection.Exists())
        {
            services.AddAuthorization();
            return services;
        }

        services.AddSingleton<IValidateOptions<ServiceAuthConfig>>(new ServiceAuthConfigValidator(environment));
        services
            .AddOptions<ServiceAuthConfig>()
            .Bind(serviceAuthSection)
            .PostConfigure(config => config.ApplyPlatformDefaults())
            .ValidateOnStart();

        var preview = serviceAuthSection.Get<ServiceAuthConfig>() ?? new ServiceAuthConfig();
        preview.ApplyPlatformDefaults();
        if (preview.IsDevelopment)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentServiceAuthHandler>(
                ServiceAuthDefaults.Scheme,
                _ => { });
        }
        else
        {
            // The signing-key document is on the public STS host, so this backchannel uses the CDP proxy.
            authentication.AddJwtBearer(ServiceAuthDefaults.Scheme, _ => { });
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ServiceJwtBearerConfigurer>();
        }

        services.AddSingleton<IAuthorizationHandler, AllowedServiceHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(ServiceAuthDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(
                    JwtBearerDefaults.AuthenticationScheme,
                    ServiceAuthDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new AllowedServiceRequirement());
            });

        return services;
    }

    private static string? CdpEnvironmentName() => Environment.GetEnvironmentVariable("ENVIRONMENT");

    [ExcludeFromCodeCoverage]
    private sealed class ServiceAuthConfigValidator(IHostEnvironment environment) : IValidateOptions<ServiceAuthConfig>
    {
        public ValidateOptionsResult Validate(string? name, ServiceAuthConfig options)
        {
            var error = options.ValidationError(environment.EnvironmentName, CdpEnvironmentName());
            return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
        }
    }
}

[ExcludeFromCodeCoverage]
internal sealed class ServiceJwtBearerConfigurer(
    IOptions<ServiceAuthConfig> serviceAuth,
    IOptions<JwtConfig> jwt,
    ILogger<ProxyHttpMessageHandler> proxyLogger) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, ServiceAuthDefaults.Scheme, StringComparison.Ordinal))
        {
            return;
        }

        var config = serviceAuth.Value;
        options.MetadataAddress = config.OidcUrl;
        options.Audience = config.Audience;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.RefreshOnIssuerKeyNotFound = true;
        options.AutomaticRefreshInterval = jwt.Value.AutomaticRefreshInterval;
        options.RefreshInterval = jwt.Value.RefreshInterval;
        // OIDC and JWKS are outside the VPC. The platform proxy is HTTP_PROXY.
        options.BackchannelHttpHandler = new ProxyHttpMessageHandler(proxyLogger);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = true,
            ValidAudience = config.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            NameClaimType = "sub",
            ValidAlgorithms = ["RS256", "ES384"]
        };
    }
}
