namespace MongoDB.Sync.Models
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Data { get; set; } = new List<string>();
        public int PageNumber { get; set; }
        public string? DatabaseName { get; set; }
        public string? CollectionName { get; set; }
        public string? LastSyncedId { get; set; }  // Used for resume functionality on disconnect
    }
}
