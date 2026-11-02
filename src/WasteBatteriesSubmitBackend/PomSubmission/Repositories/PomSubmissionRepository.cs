using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Repositories;

public interface IPomSubmissionRepository
{
    Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default);

    // The same insert, enlisted in the caller's transaction.
    // A duplicate key is thrown to the caller. The transaction callback must let it propagate so the unit of work aborts.
    Task CreateAsync(
        IClientSessionHandle session,
        PomSubmissionModel submission,
        CancellationToken cancellationToken = default);

    // Clears isCurrent and sets updatedAtUtc while this submission is still current.
    // False means it was already replaced, and nothing was written.
    // Call this before inserting the replacement. The unique index allows one current submission for the key.
    Task<bool> SupersedeCurrentAsync(
        IClientSessionHandle session,
        string id,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

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

    public Task CreateAsync(PomSubmissionModel submission, CancellationToken cancellationToken = default) =>
        InsertAsync(session: null, submission, cancellationToken);

    public Task CreateAsync(
        IClientSessionHandle session,
        PomSubmissionModel submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return InsertAsync(session, submission, cancellationToken);
    }

    public async Task<bool> SupersedeCurrentAsync(
        IClientSessionHandle session,
        string id,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var update = Builders<PomSubmissionModel>.Update
            .Set(submission => submission.IsCurrent, false)
            .Set(submission => submission.UpdatedAtUtc, updatedAtUtc);

        var result = await Collection.UpdateOneAsync(
            session,
            Filter(new PomSubmissionQuery { Id = id, IsCurrent = true }),
            update,
            options: null,
            cancellationToken);

        return result.MatchedCount == 1;
    }

    private async Task InsertAsync(
        IClientSessionHandle? session,
        PomSubmissionModel submission,
        CancellationToken cancellationToken)
    {
        try
        {
            if (session is null)
            {
                await Collection.InsertOneAsync(submission, cancellationToken: cancellationToken);
            }
            else
            {
                await Collection.InsertOneAsync(session, submission, options: null, cancellationToken);
            }
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

        if (window.IsPaged)
        {
            find = find.Skip(window.Skip).Limit(window.PageSize);
        }

        var items = await find.ToListAsync(cancellationToken);
        return new PaginatedList<PomSubmissionModel>(items, totalCount, window.PageNumber, window.PageSize);
    }

    // IsCurrent defaults to true. A query that clears it and sets nothing else would read the whole collection.
    internal static FilterDefinition<PomSubmissionModel> Filter(PomSubmissionQuery query)
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
