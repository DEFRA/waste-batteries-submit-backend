using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

// Query for the collection read. Each set filter is applied. Several filters are combined with AND.
// Repeated years match any of those years, and repeated quarters match any of those quarters.
public sealed record PomSubmissionListRequest
{
    public string? SchemeOrgId { get; init; }

    public string? MemberOrgId { get; init; }

    public int[]? CompliancePeriodYears { get; init; }

    public string[]? CompliancePeriodQuarters { get; init; }

    public bool? IsCurrent { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    public PomSubmissionListRequest Trimmed() => this with
    {
        SchemeOrgId = StringValues.TrimToNull(SchemeOrgId),
        MemberOrgId = StringValues.TrimToNull(MemberOrgId),
        CompliancePeriodQuarters = TrimQuarters(CompliancePeriodQuarters)
    };

    private static string[]? TrimQuarters(string[]? quarters) =>
        quarters?.Select(quarter => quarter?.Trim() ?? string.Empty).ToArray();
}
