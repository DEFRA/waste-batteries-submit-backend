using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Posted fields for a new filing or a later amendment. The service assigns ids,
// version, status and timestamps when it writes the document.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PomSubmissionRequest : PomSubmissionBase
{
    public List<TonnageEntryBase> TonnageEntries { get; init; } = [];
}
