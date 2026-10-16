using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Authentication;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuthenticationAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtConfig = configuration.GetRequiredSection("Jwt").Get<JwtConfig>()
                        ?? throw new InvalidOperationException("Jwt configuration is required.");

        services
            .AddOptions<JwtConfig>()
            .Bind(configuration.GetRequiredSection("Jwt"))
            .ValidateDataAnnotations()
            .Validate(config => !string.IsNullOrWhiteSpace(config.MetadataAddress), "Jwt:MetadataAddress is required.")
            .Validate(config => !string.IsNullOrWhiteSpace(config.Audience), "Jwt:Audience is required.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = jwtConfig.MetadataAddress;
                options.Audience = jwtConfig.Audience;
                options.RequireHttpsMetadata = jwtConfig.RequireHttpsMetadata;
                options.MapInboundClaims = false;
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
