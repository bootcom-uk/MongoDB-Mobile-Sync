namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionUpdateStatus
    {
        public required string DatabaseName { get; set; }
        public required string CollectionName { get; set; }
        public int RecordsToDownload { get; set; }
    }
}
