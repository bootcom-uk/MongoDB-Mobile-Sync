namespace MongoDB.Sync.Models.Web
{
    public class WebSyncCollectionUpdateInfo
    {
        public required string DatabaseName { get; set; }
        public required string CollectionName { get; set; }
        public int RecordsToDownload { get; set; }
        public bool IsInitialSyncRequired { get; set; }
        public bool ShouldRemoveLocally { get; set; }
        public bool RequiresRebuild { get; set; }
        public int CurrentVersion { get; set; }
    }
}
