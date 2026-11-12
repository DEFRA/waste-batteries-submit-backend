using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Repositories;

public interface IPomSubmissionRepository
{
    Task CreateAsync(PomSubmissionModel submission, 
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        IClientSessionHandle session,
        PomSubmissionModel submission,
        CancellationToken cancellationToken = default);

    // Inserts every submission on the caller's session. An empty list writes nothing.
    Task CreateManyAsync(
        IClientSessionHandle session,
        IReadOnlyList<PomSubmissionModel> submissions,
        CancellationToken cancellationToken = default);

    // Sets isCurrent to false and sets updatedAtUtc assuming that the submission is still current.
    // False means it was already updated, and nothing was written.
    Task<bool> SupersedeCurrentAsync(
        IClientSessionHandle session,
        string id,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    // Sets isCurrent to false and sets updatedAtUtc on each id that is still current.
    // The count is how many of those ids were still current. An empty list writes nothing.
    Task<long> SupersedeCurrentsAsync(
        IClientSessionHandle session,
        IReadOnlyCollection<string> ids,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    // Returns every submission matching the PomSubmissionFilter and PageRequest.
    // Ordered by scheme, member, then latest year, quarter and version, then newest created, then id.
    // A submission id matches at most one.
    // If Scheme, Members, Years and quarters are supplied it is an "AND", returning a list of submissions.
    Task<PaginatedList<PomSubmissionModel>> FindAsync(
        PomSubmissionFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    // The current submission for each key, read in one query. A key with no current row is absent.
    Task<IReadOnlyList<PomSubmissionModel>> FindCurrentAsync(
        IReadOnlyCollection<PomSubmissionKey> keys,
        CancellationToken cancellationToken = default);
}

public class PomSubmissionRepository(IMongoDbClientFactory connectionFactory, ILoggerFactory loggerFactory)
    : MongoService<PomSubmissionModel>(connectionFactory, CollectionName, loggerFactory), IPomSubmissionRepository
{
    public const string CollectionName = "pom-submissions";

    // MongoDB duplicate key. The driver usually reports this as ServerErrorCategory.DuplicateKey.
    private const int DuplicateKeyCode = 11000;

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

    public Task CreateManyAsync(
        IClientSessionHandle session,
        IReadOnlyList<PomSubmissionModel> submissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(submissions);

        if (submissions.Count == 0)
        {
            return Task.CompletedTask;
        }

        return InsertManyAsync(session, submissions, cancellationToken);
    }

    public async Task<bool> SupersedeCurrentAsync(
        IClientSessionHandle session,
        string id,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var result = await Collection.UpdateOneAsync(
            session,
            Filter(new PomSubmissionFilter { Id = id, IsCurrent = true }),
            SupersedeUpdate(updatedAtUtc),
            options: null,
            cancellationToken);

        return result.MatchedCount == 1;
    }

    public async Task<long> SupersedeCurrentsAsync(
        IClientSessionHandle session,
        IReadOnlyCollection<string> ids,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return 0;
        }

        var filter = Builders<PomSubmissionModel>.Filter.And(
            Builders<PomSubmissionModel>.Filter.In(submission => submission.Id, ids),
            Builders<PomSubmissionModel>.Filter.Eq(submission => submission.IsCurrent, true));

        var result = await Collection.UpdateManyAsync(
            session,
            filter,
            SupersedeUpdate(updatedAtUtc),
            options: null,
            cancellationToken);

        return result.MatchedCount;
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
        catch (Exception exception)
        {
            // The only unique index is one current submission per scheme, member, year and quarter.
            if (ToDuplicateKey(exception) is { } duplicate)
            {
                throw duplicate;
            }

            throw;
        }
    }

    private async Task InsertManyAsync(
        IClientSessionHandle session,
        IReadOnlyList<PomSubmissionModel> submissions,
        CancellationToken cancellationToken)
    {
        try
        {
            await Collection.InsertManyAsync(session, submissions, options: null, cancellationToken);
        }
        catch (Exception exception)
        {
            if (ToDuplicateKey(exception) is { } duplicate)
            {
                throw duplicate;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<PomSubmissionModel>> FindCurrentAsync(
        IReadOnlyCollection<PomSubmissionKey> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return [];
        }

        return await Collection.Find(FilterCurrent(keys)).ToListAsync(cancellationToken);
    }

    public async Task<PaginatedList<PomSubmissionModel>> FindAsync(
        PomSubmissionFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var definition = Filter(filter);
        var totalCount = (int)await Collection.CountDocumentsAsync(definition, cancellationToken: cancellationToken);
        var window = PageWindow.Resolve(page.PageNumber, page.PageSize, totalCount);
        var find = Collection.Find(definition).Sort(ListOrder());

        if (window.IsPaged)
        {
            find = find.Skip(window.Skip).Limit(window.PageSize);
        }

        var items = await find.ToListAsync(cancellationToken);
        return new PaginatedList<PomSubmissionModel>(items, totalCount, window.PageNumber, window.PageSize);
    }

    // Scheme and member ascending. Within a member, the latest year, quarter and version come first,
    // then the most recently created row, then id.
    internal static SortDefinition<PomSubmissionModel> ListOrder() =>
        Builders<PomSubmissionModel>.Sort
            .Ascending(submission => submission.SchemeOrgId)
            .Ascending(submission => submission.MemberOrgId)
            .Descending(submission => submission.CompliancePeriodYear)
            .Descending(submission => submission.CompliancePeriodQuarter)
            .Descending(submission => submission.Version)
            .Descending(submission => submission.CreatedAtUtc)
            .Ascending(submission => submission.Id);

    // IsCurrent defaults to true. A filter that clears it and sets nothing else would read the whole collection.
    internal static FilterDefinition<PomSubmissionModel> Filter(PomSubmissionFilter filter)
    {
        var builder = Builders<PomSubmissionModel>.Filter;
        var filters = new List<FilterDefinition<PomSubmissionModel>>();

        if (filter.Id is not null)
        {
            filters.Add(builder.Eq(submission => submission.Id, filter.Id));
        }

        if (filter.SchemeOrgId is not null)
        {
            filters.Add(builder.Eq(submission => submission.SchemeOrgId, filter.SchemeOrgId));
        }

        if (filter.MemberOrgId is not null)
        {
            filters.Add(builder.Eq(submission => submission.MemberOrgId, filter.MemberOrgId));
        }

        if (filter.CompliancePeriodYears is { Count: > 0 })
        {
            filters.Add(builder.In(submission => submission.CompliancePeriodYear, filter.CompliancePeriodYears));
        }

        if (filter.CompliancePeriodQuarters is { Count: > 0 })
        {
            filters.Add(builder.In(submission => submission.CompliancePeriodQuarter, filter.CompliancePeriodQuarters));
        }

        if (filter.IsCurrent is bool isCurrent)
        {
            filters.Add(builder.Eq(submission => submission.IsCurrent, isCurrent));
        }

        if (filters.Count == 0)
        {
            throw new ArgumentException("A submission filter needs at least one criterion.", nameof(filter));
        }

        return builder.And(filters);
    }

    // One query for these current rows using the keys (scheme id, member id, year & quarter).
    // An empty key list is rejected so this cannot become a collection scan.
    internal static FilterDefinition<PomSubmissionModel> FilterCurrent(IReadOnlyCollection<PomSubmissionKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one submission key is required.", nameof(keys));
        }

        var builder = Builders<PomSubmissionModel>.Filter;
        var matches = new List<FilterDefinition<PomSubmissionModel>>(keys.Count);
        foreach (var key in keys)
        {
            matches.Add(builder.And(
                builder.Eq(submission => submission.SchemeOrgId, key.SchemeOrgId),
                builder.Eq(submission => submission.MemberOrgId, key.MemberOrgId),
                builder.Eq(submission => submission.CompliancePeriodYear, key.CompliancePeriodYear),
                builder.Eq(submission => submission.CompliancePeriodQuarter, key.CompliancePeriodQuarter)));
        }

        return builder.And(
            builder.Eq(submission => submission.IsCurrent, true),
            builder.Or(matches));
    }

    private static UpdateDefinition<PomSubmissionModel> SupersedeUpdate(DateTime updatedAtUtc) =>
        Builders<PomSubmissionModel>.Update
            .Set(submission => submission.IsCurrent, false)
            .Set(submission => submission.UpdatedAtUtc, updatedAtUtc);

    // A duplicate on the current-submission index. A bulk insert keeps the position of the failing document.
    private static DuplicateKeyOnSaveException? ToDuplicateKey(Exception exception) =>
        exception switch
        {
            MongoWriteException write when write.WriteError?.Category == ServerErrorCategory.DuplicateKey
                => new DuplicateKeyOnSaveException(write),
            MongoBulkWriteException bulk when FirstDuplicateIndex(bulk) is int index
                => new DuplicateKeyOnSaveException(bulk, index),
            MongoCommandException command when command.Code == DuplicateKeyCode
                => new DuplicateKeyOnSaveException(command),
            _ => null
        };

    private static int? FirstDuplicateIndex(MongoBulkWriteException exception)
    {
        foreach (var error in exception.WriteErrors)
        {
            if (error.Category == ServerErrorCategory.DuplicateKey || error.Code == DuplicateKeyCode)
            {
                return error.Index;
            }
        }

        return null;
    }

    // Retention is a floor of 4 years, so this collection has no TTL index.
    protected override List<CreateIndexModel<PomSubmissionModel>> DefineIndexes(IndexKeysDefinitionBuilder<PomSubmissionModel> builder)
        => PomSubmissionIndexes.Create(builder);
}
