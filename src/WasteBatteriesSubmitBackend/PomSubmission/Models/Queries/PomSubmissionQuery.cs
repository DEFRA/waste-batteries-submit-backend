namespace WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;

// Criteria for reading submissions. Only the fields that are set are applied, and they are combined with AND.
// A year list or a quarter list matches any of its values.
// The collection read sets any mixture of the organisation, year and quarter fields.
// Id is the document read, which clears IsCurrent so a submission that is no longer current is still returned.
public sealed record PomSubmissionQuery
{
    public string? Id { get; init; }

    public string? SchemeOrgId { get; init; }

    public string? MemberOrgId { get; init; }

    public IReadOnlyList<int>? CompliancePeriodYears { get; init; }

    public IReadOnlyList<string>? CompliancePeriodQuarters { get; init; }

    // Null leaves the field unset. The default keeps a read to the current submissions.
    public bool? IsCurrent { get; init; } = true;
}
