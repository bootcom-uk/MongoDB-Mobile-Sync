namespace MongoDB.Sync.Client.Interfaces;

public interface ILocalDocumentStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken token = default);

    /// <summary>
    /// Applies a batch atomically. Must be transaction-wrapped by the provider.
    /// </summary>
    Task ApplyBatchAsync(IReadOnlyList<ISyncDocument> documents, CancellationToken token = default);

    Task<ISyncDocument?> GetByIdAsync(string collection, string id, bool includeDeleted = false, CancellationToken token = default);

    Task<IReadOnlyList<ISyncDocument>> GetByIdsAsync(string collection, IReadOnlyList<string> ids, bool includeDeleted = false, CancellationToken token = default);

    Task<IReadOnlyList<ISyncDocument>> GetCollectionAsync(string collection, bool includeDeleted = false, CancellationToken token = default);

    /// <summary>
    /// Used for incremental refresh / debugging: returns docs with UpdatedAt > since.
    /// </summary>
    Task<IReadOnlyList<ISyncDocument>> GetChangesSinceAsync(string collection, DateTime since, bool includeDeleted = true, CancellationToken token = default);

    Task<ICollectionCheckpoint> GetCheckpointAsync(string collection, CancellationToken token = default);

    Task SetCheckpointAsync(ICollectionCheckpoint checkpoint, CancellationToken token = default);

    Task ClearAsync(CancellationToken token = default);
}