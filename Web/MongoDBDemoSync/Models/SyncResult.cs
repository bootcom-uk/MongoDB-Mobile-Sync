using MongoDB.Bson;

namespace MongoDBDemoSync.Models
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Data { get; set; } = new List<string>(); // Store as JSON strings
        public int PageNumber { get; set; }

        public int Count { get; set; }

        public string AppName { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = string.Empty;

        public string? LastSyncedId { get; set; } = string.Empty; // ID of last document synced
    }
}
