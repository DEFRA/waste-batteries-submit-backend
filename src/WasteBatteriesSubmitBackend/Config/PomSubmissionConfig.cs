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

    // Submissions in one list response when the caller does not accept gzip.
    // The calculation is in the README. Recalculate both limits if PomSubmissionModel changes.
    [Range(1, int.MaxValue)]
    public int MaxPageSize { get; init; }

    // Submissions in one list response when the caller accepts gzip. See the README.
    [Range(1, int.MaxValue)]
    public int MaxGzipPageSize { get; init; }
}
