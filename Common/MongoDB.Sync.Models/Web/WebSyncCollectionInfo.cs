namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionInfo
    {
        public required string DatabaseName { get; set; }
        public required string CollectionName { get; set; }
        public DateTime? LastSyncDate { get; set; }
        public int? CollectionVersion { get; set; }
    }
}
