using MongoDB.Driver;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

// The session for one transaction. It is valid only until ExecuteAsync returns.
// Repository writes take Session so they commit or roll back together. A later outbox insert uses the same session.
public interface IMongoTransaction
{
    IClientSessionHandle Session { get; }
}

public interface IUnitOfWork
{
    // Commits when work returns. Aborts when work throws.
    // The driver retries the whole callback on TransientTransactionError and UnknownTransactionCommitResult.
    // Work must only write through this transaction, and ids and timestamps must be allocated before ExecuteAsync.
    // Multi-document transactions require a replica set. A standalone mongod rejects them.
    Task<T> ExecuteAsync<T>(
        Func<IMongoTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}

public sealed class UnitOfWork(IMongoDbClientFactory clientFactory) : IUnitOfWork
{
    // Snapshot plus majority is what a multi-document transaction uses to make the two writes atomic.
    private static readonly TransactionOptions TransactionOptions = new(
        readConcern: ReadConcern.Snapshot,
        readPreference: ReadPreference.Primary,
        writeConcern: WriteConcern.WMajority);

    public async Task<T> ExecuteAsync<T>(
        Func<IMongoTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        using var session = await clientFactory.GetClient().StartSessionAsync(null, cancellationToken);
        return await session.WithTransactionAsync(
            (handle, transactionCancellationToken) =>
                work(new MongoTransaction(handle), transactionCancellationToken),
            TransactionOptions,
            cancellationToken);
    }

    private sealed class MongoTransaction(IClientSessionHandle session) : IMongoTransaction
    {
        public IClientSessionHandle Session { get; } = session;
    }
}
