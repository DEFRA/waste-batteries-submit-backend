using System.Text.Json.Serialization;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Posted fields for a new submission or a later amendment. The service assigns ids,
// version, status and timestamps when it writes the document.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PomSubmissionRequest : PomSubmissionBase
{
    public List<TonnageEntryBase> TonnageEntries { get; init; } = [];

    public PomSubmissionRequest Trimmed() => this with
    {
        SchemeOrgId = StringValues.TrimToEmpty(SchemeOrgId),
        MemberOrgId = StringValues.TrimToEmpty(MemberOrgId),
        MemberName = StringValues.TrimToEmpty(MemberName),
        MemberBprn = StringValues.TrimToEmpty(MemberBprn),
        SchemeMembershipRef = StringValues.TrimToEmpty(SchemeMembershipRef),
        CompliancePeriodQuarter = StringValues.TrimToEmpty(CompliancePeriodQuarter),
        DataBasis = StringValues.TrimToEmpty(DataBasis),
        TonnageEntries = TonnageEntries?.ConvertAll(entry => entry is null ? null! : entry.Trimmed())!
    };
}
