using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using WasteBatteriesSubmitBackend.Utils.Json;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Header fields posted by a scheme and stored on the submission.
// The tonnage list is not here: the request entries have no id, and the stored entries do.
// The compiler-generated copy constructor copies these fields onto a PomSubmissionModel.
public abstract record PomSubmissionBase
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string SchemeOrgId { get; init; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string MemberOrgId { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string MemberBprn { get; init; } = string.Empty;

    public string SchemeMembershipRef { get; init; } = string.Empty;

    [IntJsonConverter("compliancePeriodYear")]
    public int CompliancePeriodYear { get; init; }

    public string CompliancePeriodQuarter { get; init; } = string.Empty;

    public string DataBasis { get; init; } = string.Empty;
}
