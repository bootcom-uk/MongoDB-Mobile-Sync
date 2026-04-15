using MongoDB.Sync.Client.Interfaces;

namespace MongoDB.Sync.Client.Models
{

    public sealed class SyncDocument
    {
        // Store ObjectId.ToString() (24-char hex) here.
        public required string Id { get; init; }

        public required string Collection { get; init; }

        // Canonical JSON payload for the document.
        public required string Json { get; init; }

        // Server-side marker used for diffs/resume.
        public required DateTime UpdatedAt { get; init; }

        // Tombstone.
        public bool IsDeleted { get; init; }
    }
}
