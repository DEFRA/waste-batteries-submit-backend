using WasteBatteriesSubmitBackend.Utils.Logging;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

[ExcludeFromCodeCoverage]
public abstract class MongoService<T>
{
    protected readonly IMongoClient Client;
    protected readonly IMongoCollection<T> Collection;

    protected readonly ILogger Logger;

    protected MongoService(IMongoDbClientFactory connectionFactory, string collectionName, ILoggerFactory loggerFactory)
    {
        Client = connectionFactory.GetClient();
        Collection = connectionFactory.GetCollection<T>(collectionName);
        var loggerName = GetType().FullName ?? GetType().Name;
        Logger = loggerFactory.CreateLogger(loggerName);
        EnsureIndexes();
    }

    protected abstract List<CreateIndexModel<T>> DefineIndexes(IndexKeysDefinitionBuilder<T> builder);

    protected void EnsureIndexes()
    {
        var builder = Builders<T>.IndexKeys;
        var indexes = DefineIndexes(builder);
        if (indexes.Count == 0) return;

        Logger.EnsuringIndexes(
            Collection.CollectionNamespace.CollectionName,
            Collection.Database.DatabaseNamespace.DatabaseName);
        Collection.Indexes.CreateMany(indexes);
    }
}