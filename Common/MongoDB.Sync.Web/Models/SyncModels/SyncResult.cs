namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class SyncResult
    {
        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public List<string> Data { get; set; } = new List<string>();

        public int PageNumber { get; set; }

        public int Count { get; set; }

        public string AppName { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = string.Empty;

        public string? LastSyncedId { get; set; } = string.Empty;
    }
}
