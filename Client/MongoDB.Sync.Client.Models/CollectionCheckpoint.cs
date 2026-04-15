using MongoDB.Sync.Client.Interfaces;

namespace MongoDB.Sync.Client.Models;

public sealed class CollectionCheckpoint : ICollectionCheckpoint
{
    public required string Collection { get; init; }

    // Last UpdatedAt successfully persisted for this collection.
    public DateTime? LastUpdatedAt { get; init; }
}
