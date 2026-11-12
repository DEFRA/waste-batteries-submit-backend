using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// The current-submission key: one current row for a scheme, member, year and quarter.
// Object ids are canonical, so a different hex case still matches the stored id.
public readonly record struct PomSubmissionKey(
    string SchemeOrgId,
    string MemberOrgId,
    int CompliancePeriodYear,
    string CompliancePeriodQuarter)
{
    public static PomSubmissionKey From(PomSubmissionBase submission) =>
        new(
            ObjectId.Parse(submission.SchemeOrgId).ToString(),
            ObjectId.Parse(submission.MemberOrgId).ToString(),
            submission.CompliancePeriodYear,
            submission.CompliancePeriodQuarter);
}
