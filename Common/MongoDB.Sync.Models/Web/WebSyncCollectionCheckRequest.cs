namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionCheckRequest
    {
        public required string AppName { get; set; }
        public required List<WebSyncCollectionInfo> Collections { get; set; }
    }
}
