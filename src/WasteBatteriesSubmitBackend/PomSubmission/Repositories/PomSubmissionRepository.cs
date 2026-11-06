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

    // Sets isCurrent to false and sets updatedAtUtc assuming that the submission is still current.
    // False means it was already updated, and nothing was written.
    Task<bool> SupersedeCurrentAsync(
        IClientSessionHandle session,
        string id,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    // Returns every submission matching the PomSubmissionFilter and PageRequest.
    // A submission id matches at most one.
    // If Scheme, Members, Years and quarters are supplied it is an "AND", returning a list of submissions.
    Task<PaginatedList<PomSubmissionModel>> FindAsync(
        PomSubmissionFilter filter,
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
            Filter(new PomSubmissionFilter { Id = id, IsCurrent = true }),
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
        PomSubmissionFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var definition = Filter(filter);
        var totalCount = (int)await Collection.CountDocumentsAsync(definition, cancellationToken: cancellationToken);
        var window = PageWindow.Resolve(page.PageNumber, page.PageSize, totalCount);
        var find = Collection.Find(definition);
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

    // Retention is a floor of 4 years, so this collection has no TTL index.
    protected override List<CreateIndexModel<PomSubmissionModel>> DefineIndexes(IndexKeysDefinitionBuilder<PomSubmissionModel> builder)
        => PomSubmissionIndexes.Create(builder);
}
