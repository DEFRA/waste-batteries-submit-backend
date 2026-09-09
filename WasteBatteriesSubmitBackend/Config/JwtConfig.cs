using System.ComponentModel.DataAnnotations;

namespace WasteBatteriesSubmitBackend.Config;

public sealed class JwtConfig
{
    [Required]
    public string MetadataAddress { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;

    [Required]
    public string UserIdClaim { get; init; } = "sub";
}
