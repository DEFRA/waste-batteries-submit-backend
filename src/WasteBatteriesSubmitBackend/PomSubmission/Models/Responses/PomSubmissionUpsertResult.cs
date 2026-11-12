using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;

// One saved item from a batch upsert. Tonnage and the other stored fields stay off the response.
public sealed record PomSubmissionUpsertResult
{
    // Position in the posted array. A validation error for that item uses the same index, for example [0].MemberName.
    public int RequestIndex { get; init; }

    // The submission just saved. An amendment's id is the new version, not the row it replaced.
    public required string Id { get; init; }

    public required string SchemeOrgId { get; init; }

    public required string MemberOrgId { get; init; }

    public int CompliancePeriodYear { get; init; }

    public required string CompliancePeriodQuarter { get; init; }

    // Created when no current row existed for this key. Amended when one did.
    [JsonConverter(typeof(JsonStringEnumConverter<PomSubmissionUpsertAction>))]
    public PomSubmissionUpsertAction Action { get; init; }
}

public enum PomSubmissionUpsertAction
{
    Created,
    Amended
}
