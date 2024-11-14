namespace MongoDB.Sync.MAUI.Models
{
    public class SyncOptions
    {
        public string LiteDbPath { get; set; } = Path.Combine(FileSystem.AppDataDirectory, "syncData.db");
        public string ApiUrl { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;

    }
}
