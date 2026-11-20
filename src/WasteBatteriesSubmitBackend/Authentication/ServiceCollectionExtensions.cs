using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Config;

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
        IConfiguration configuration)
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

        services
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

        services.AddAuthorization();

        return services;
    }
}
