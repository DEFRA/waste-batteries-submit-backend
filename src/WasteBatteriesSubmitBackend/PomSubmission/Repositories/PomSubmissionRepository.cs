using System.Diagnostics.CodeAnalysis;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Repositories;

public interface IPomSubmissionRepository
{
    Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default);

    Task<PomSubmissionModel?> FindCurrentAsync(
        string schemeOrgId,
        string memberOrgId,
        int compliancePeriodYear,
        string compliancePeriodQuarter,
        CancellationToken cancellationToken = default);
}

[ExcludeFromCodeCoverage]
public class PomSubmissionRepository(IMongoDbClientFactory connectionFactory, ILoggerFactory loggerFactory)
    : MongoService<PomSubmissionModel>(connectionFactory, CollectionName, loggerFactory), IPomSubmissionRepository
{
    public const string CollectionName = "pom-submissions";

    public async Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default)
    {
        await Collection.InsertOneAsync(submission, cancellationToken: cancellationToken);
    }

    public async Task<PomSubmissionModel?> FindCurrentAsync(
        string schemeOrgId,
        string memberOrgId,
        int compliancePeriodYear,
        string compliancePeriodQuarter,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PomSubmissionModel>.Filter.Eq(submission => submission.SchemeOrgId, schemeOrgId)
            & Builders<PomSubmissionModel>.Filter.Eq(submission => submission.MemberOrgId, memberOrgId)
            & Builders<PomSubmissionModel>.Filter.Eq(submission => submission.CompliancePeriodYear, compliancePeriodYear)
            & Builders<PomSubmissionModel>.Filter.Eq(submission => submission.CompliancePeriodQuarter, compliancePeriodQuarter)
            & Builders<PomSubmissionModel>.Filter.Eq(submission => submission.IsCurrent, true);

        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    // Retention is a floor of 4 years, so this collection has no TTL index.
    protected override List<CreateIndexModel<PomSubmissionModel>> DefineIndexes(IndexKeysDefinitionBuilder<PomSubmissionModel> builder)
        => PomSubmissionIndexes.Create(builder);
}
