namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionStatus
    {
        public string CollectionName { get; set; } = null!;
        public DateTime? LastSyncDate { get; set; }
        public int? Version { get; set; } // Optional: null = no idea
    }
}
