namespace WasteBatteriesSubmitBackend.Config;

// How another CDP service proves who it is. Users still use Jwt (Defra ID).
// Aws checks an STS web identity token. Development accepts the shared local token,
// because Floci cannot issue GetWebIdentityToken.
public sealed class ServiceAuthConfig
{
    public const string SectionName = "ServiceAuth";

    public const string AwsMode = "Aws";

    public const string DevelopmentMode = "Development";

    public string Mode { get; set; } = AwsMode;

    // aud claim. Obligations asks STS for this same audience.
    public string Audience { get; set; } = string.Empty;

    // principal_tags.Service, or the role name on the end of sub.
    public string AllowedService { get; set; } = string.Empty;

    // CDP_JWT_ISSUER when Issuer is empty.
    public string Issuer { get; set; } = string.Empty;

    // CDP_JWT_OIDC_URL when OidcUrl is empty. JwtBearer reads the JWKS address from this document.
    public string OidcUrl { get; set; } = string.Empty;

    // CDP_JWT_JWKS_URI when JwksUri is empty. Kept so the platform value is visible in config.
    public string JwksUri { get; set; } = string.Empty;

    // Shared with the obligations API. Used only when Mode is Development.
    public string DevToken { get; set; } = string.Empty;

    public bool IsDevelopment => string.Equals(Mode, DevelopmentMode, StringComparison.Ordinal);

    public void ApplyPlatformDefaults()
    {
        Issuer = FirstNonEmpty(Issuer, Environment.GetEnvironmentVariable("CDP_JWT_ISSUER"));
        OidcUrl = FirstNonEmpty(OidcUrl, Environment.GetEnvironmentVariable("CDP_JWT_OIDC_URL"));
        JwksUri = FirstNonEmpty(JwksUri, Environment.GetEnvironmentVariable("CDP_JWT_JWKS_URI"));
    }

    public string? ValidationError(string aspNetEnvironment, string? cdpEnvironment)
    {
        if (!string.Equals(Mode, AwsMode, StringComparison.Ordinal)
            && !string.Equals(Mode, DevelopmentMode, StringComparison.Ordinal))
        {
            return "ServiceAuth:Mode must be Aws or Development.";
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            return "ServiceAuth:Audience is required.";
        }

        if (string.IsNullOrWhiteSpace(AllowedService))
        {
            return "ServiceAuth:AllowedService is required.";
        }

        if (!IsDevelopment)
        {
            if (string.IsNullOrWhiteSpace(Issuer))
            {
                return "ServiceAuth:Issuer is required when Mode is Aws. CDP sets CDP_JWT_ISSUER.";
            }

            if (!IsHttps(OidcUrl))
            {
                return "ServiceAuth:OidcUrl must be an https URL when Mode is Aws. CDP sets CDP_JWT_OIDC_URL.";
            }

            return null;
        }

        if (!string.Equals(aspNetEnvironment, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return "ServiceAuth:Mode cannot be Development unless the host environment is Development.";
        }

        if (string.Equals(cdpEnvironment?.Trim(), "prod", StringComparison.OrdinalIgnoreCase))
        {
            return "ServiceAuth:Mode cannot be Development when ENVIRONMENT is prod.";
        }

        if (string.IsNullOrWhiteSpace(DevToken))
        {
            return "ServiceAuth:DevToken is required when Mode is Development.";
        }

        return null;
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string FirstNonEmpty(string current, string? fallback) =>
        string.IsNullOrWhiteSpace(current) ? fallback?.Trim() ?? string.Empty : current;
}
