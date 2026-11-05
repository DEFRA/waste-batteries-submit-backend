using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

// Filters and page for the collection GET. Each set filter is applied. Several filters are combined with AND.
// Repeated years match any of those years, and repeated quarters match any of those quarters.
// Query names are the public filter names. The list endpoint binds this type with AsParameters.
// The page stays on this request. PomSubmissionFilter is the same filters without it.
public sealed record PomSubmissionFilterRequest
{
    [FromQuery(Name = "schemeOrgId")]
    public string? SchemeOrgId { get; init; }

    [FromQuery(Name = "memberOrgId")]
    public string? MemberOrgId { get; init; }

    [FromQuery(Name = "compliancePeriodYears")]
    public int[]? CompliancePeriodYears { get; init; }

    [FromQuery(Name = "compliancePeriodQuarters")]
    public string[]? CompliancePeriodQuarters { get; init; }

    [FromQuery(Name = "isCurrent")]
    public bool? IsCurrent { get; init; }

    [FromQuery(Name = "pageNumber")]
    public int? PageNumber { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }

    public PomSubmissionFilterRequest Trimmed() => this with
    {
        SchemeOrgId = StringValues.TrimToNull(SchemeOrgId),
        MemberOrgId = StringValues.TrimToNull(MemberOrgId),
        CompliancePeriodQuarters = TrimQuarters(CompliancePeriodQuarters)
    };

    private static string[]? TrimQuarters(string[]? quarters) =>
        quarters?.Select(quarter => quarter?.Trim() ?? string.Empty).ToArray();
}
