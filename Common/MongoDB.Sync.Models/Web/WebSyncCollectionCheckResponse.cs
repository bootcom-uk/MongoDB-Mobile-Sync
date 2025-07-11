namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionCheckResponse
    {
        public required List<WebSyncCollectionUpdateStatus> Updates { get; set; }
    }
}
