using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Repositories;

public interface IPomSubmissionRepository
{
    Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default);

    // Every submission matching the set criteria, in pages when a page size is set.
    // An id matches at most one. Years and quarters match any value in the list.
    Task<PaginatedList<PomSubmissionModel>> FindAsync(
        PomSubmissionQuery query,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

public class PomSubmissionRepository(IMongoDbClientFactory connectionFactory, ILoggerFactory loggerFactory)
    : MongoService<PomSubmissionModel>(connectionFactory, CollectionName, loggerFactory), IPomSubmissionRepository
{
    public const string CollectionName = "pom-submissions";

    public async Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default)
    {
        try
        {
            await Collection.InsertOneAsync(submission, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The only unique index is one current submission per scheme, member, year and quarter.
            throw new DuplicateKeyOnSaveException(exception);
        }
    }

    public async Task<PaginatedList<PomSubmissionModel>> FindAsync(
        PomSubmissionQuery query,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var filter = Filter(query);
        var totalCount = (int)await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var window = PageWindow.Resolve(page.PageNumber, page.PageSize, totalCount);
        var find = Collection.Find(filter);
        find = find
            .SortByDescending(submission => submission.CreatedAtUtc)
            .ThenByDescending(submission => submission.Id);

        if (!window.IncludesAll)
        {
            find = find.Skip(window.Skip).Limit(window.PageSize);
        }

        var items = await find.ToListAsync(cancellationToken);
        return new PaginatedList<PomSubmissionModel>(items, totalCount, window.PageNumber, window.PageSize);
    }

    // IsCurrent defaults to true. A query that clears it and sets nothing else would read the whole collection.
    private static FilterDefinition<PomSubmissionModel> Filter(PomSubmissionQuery query)
    {
        var builder = Builders<PomSubmissionModel>.Filter;
        var filters = new List<FilterDefinition<PomSubmissionModel>>();

        if (query.Id is not null)
        {
            filters.Add(builder.Eq(submission => submission.Id, query.Id));
        }

        if (query.SchemeOrgId is not null)
        {
            filters.Add(builder.Eq(submission => submission.SchemeOrgId, query.SchemeOrgId));
        }

        if (query.MemberOrgId is not null)
        {
            filters.Add(builder.Eq(submission => submission.MemberOrgId, query.MemberOrgId));
        }

        if (query.CompliancePeriodYears is { Count: > 0 })
        {
            filters.Add(builder.In(submission => submission.CompliancePeriodYear, query.CompliancePeriodYears));
        }

        if (query.CompliancePeriodQuarters is { Count: > 0 })
        {
            filters.Add(builder.In(submission => submission.CompliancePeriodQuarter, query.CompliancePeriodQuarters));
        }

        if (query.IsCurrent is bool isCurrent)
        {
            filters.Add(builder.Eq(submission => submission.IsCurrent, isCurrent));
        }

        if (filters.Count == 0)
        {
            throw new ArgumentException("A submission query needs at least one criterion.", nameof(query));
        }

        return builder.And(filters);
    }

    // Retention is a floor of 4 years, so this collection has no TTL index.
    protected override List<CreateIndexModel<PomSubmissionModel>> DefineIndexes(IndexKeysDefinitionBuilder<PomSubmissionModel> builder)
        => PomSubmissionIndexes.Create(builder);
}
