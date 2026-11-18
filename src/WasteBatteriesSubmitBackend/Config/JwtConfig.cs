using System.ComponentModel.DataAnnotations;

namespace WasteBatteriesSubmitBackend.Config;

public sealed class JwtConfig
{
    // IdentityModel refuses a shorter automatic refresh.
    public static readonly TimeSpan MinimumAutomaticRefreshInterval = TimeSpan.FromMinutes(5);

    // IdentityModel refuses a shorter gap between forced refreshes.
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(1);

    [Required]
    public string MetadataAddress { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;

    [Required]
    public string UserIdClaim { get; init; } = "sub";

    // Re-reads the issuer signing keys on this cadence. See appsettings.json.
    public TimeSpan AutomaticRefreshInterval { get; init; } = MinimumAutomaticRefreshInterval;

    // Minimum wait after a missing signing key before another metadata read. See appsettings.json.
    public TimeSpan RefreshInterval { get; init; } = MinimumRefreshInterval;
}
