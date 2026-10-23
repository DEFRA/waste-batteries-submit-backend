using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.PomSubmission.Repositories;

internal static class PomSubmissionIndexes
{
    public static List<CreateIndexModel<PomSubmissionModel>> Create(IndexKeysDefinitionBuilder<PomSubmissionModel> builder)
    {
        var currentPerMemberQuarter = new CreateIndexModel<PomSubmissionModel>(
            builder
                .Ascending(submission => submission.SchemeOrgId)
                .Ascending(submission => submission.MemberOrgId)
                .Ascending(submission => submission.CompliancePeriodYear)
                .Ascending(submission => submission.CompliancePeriodQuarter),
            new CreateIndexOptions<PomSubmissionModel>
            {
                Unique = true,
                PartialFilterExpression = Builders<PomSubmissionModel>.Filter.Eq(submission => submission.IsCurrent, true)
            });

        var schemeQuarter = new CreateIndexModel<PomSubmissionModel>(
            builder
                .Ascending(submission => submission.SchemeOrgId)
                .Ascending(submission => submission.CompliancePeriodYear)
                .Ascending(submission => submission.CompliancePeriodQuarter));

        var memberHistory = new CreateIndexModel<PomSubmissionModel>(
            builder
                .Ascending(submission => submission.MemberOrgId)
                .Ascending(submission => submission.CompliancePeriodYear),
            new CreateIndexOptions<PomSubmissionModel>
            {
                PartialFilterExpression = Builders<PomSubmissionModel>.Filter.Eq(submission => submission.IsCurrent, true)
            });

        var memberBprn = new CreateIndexModel<PomSubmissionModel>(builder.Ascending(submission => submission.MemberBprn));

        var chemistry = new CreateIndexModel<PomSubmissionModel>(
            builder
                .Ascending("tonnageEntries.classification.chemistry")
                .Ascending("tonnageEntries.classification.batteryCategory"));

        var supersedes = new CreateIndexModel<PomSubmissionModel>(
            builder.Ascending(submission => submission.SupersedesSubmissionId),
            new CreateIndexOptions
            {
                Sparse = true
            });

        return
        [
            currentPerMemberQuarter,
            schemeQuarter,
            memberHistory,
            memberBprn,
            chemistry,
            supersedes
        ];
    }
}
