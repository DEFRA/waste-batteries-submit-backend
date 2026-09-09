using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Utils.Auditing;

namespace WasteBatteriesSubmitBackend.ExampleData.Services;

public interface IExampleDataPersistence
{
    Task<IReadOnlyCollection<ExampleDataModel>> GetAllAsync(string? userId, CancellationToken cancellationToken = default);

    Task<ExampleDataModel?> GetByIdAsync(string exampleId, CancellationToken cancellationToken = default);

    Task<ExampleDataModel> SaveAsync(string exampleText, string userId, CancellationToken cancellationToken = default);
}


[ExcludeFromCodeCoverage]
public class ExampleDataPersistence(IMongoDbClientFactory connectionFactory, ILoggerFactory loggerFactory)
    : MongoService<ExampleDataModel>(connectionFactory, "example-data", loggerFactory), IExampleDataPersistence
{
    public async Task<IReadOnlyCollection<ExampleDataModel>> GetAllAsync(string? userId, CancellationToken cancellationToken = default)
    {
        var filter = userId is null
            ? Builders<ExampleDataModel>.Filter.Empty
            : Builders<ExampleDataModel>.Filter.Eq(e => e.UserId, userId);

        return await Collection
            .Find(filter)
            .SortByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExampleDataModel?> GetByIdAsync(string exampleId, CancellationToken cancellationToken = default)
    {
        var result = await Collection.Find(e => e.Id == exampleId).FirstOrDefaultAsync(cancellationToken);
        Logger.LogInformation("Searching for {ExampleId}, found {Result}", exampleId, result);
        return result;
    }

    public async Task<ExampleDataModel> SaveAsync(string exampleText, string userId, CancellationToken cancellationToken = default)
    {
        var existing = await Collection.Find(e => e.UserId == userId).FirstOrDefaultAsync(cancellationToken);

        var document = new ExampleDataModel
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            ExampleText = exampleText,
            UserId = userId,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };

        // Demo write path collapses old duplicate rows; add a migration and a
        // unique index when this becomes real persisted user data.
        await Collection.DeleteManyAsync(e => e.UserId == userId, cancellationToken);
        await Collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        Logger.Audit("Saved example text for user: {UserId}", userId);

        return document;
    }

    [ExcludeFromCodeCoverage]
    protected override List<CreateIndexModel<ExampleDataModel>> DefineIndexes(
        IndexKeysDefinitionBuilder<ExampleDataModel> builder)
    {
        var userIdIndex = new CreateIndexModel<ExampleDataModel>(builder.Ascending(e => e.UserId));
        return [userIdIndex];
    }
}
