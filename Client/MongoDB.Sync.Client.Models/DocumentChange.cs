using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Models;

public enum ChangeType
{
    Upserted = 1,
    Deleted = 2
}

public sealed class DocumentChange
{
    public required string Collection { get; init; }
    public required string Id { get; init; }
    public required ChangeType ChangeType { get; init; }
    public DateTime? UpdatedAt { get; init; }               // helpful for caches
}

