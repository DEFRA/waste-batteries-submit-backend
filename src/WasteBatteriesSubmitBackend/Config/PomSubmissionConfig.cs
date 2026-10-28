namespace WasteBatteriesSubmitBackend.Config;

using System.ComponentModel.DataAnnotations;

public class PomSubmissionConfig
{
    [Required]
    public required string InitialStatus { get; init; }

    [Range(1, int.MaxValue)]
    public int InitialVersion { get; init; }

    [Required]
    public required string InitialActivity { get; init; }
}
