using Services;

namespace MongoDB.Sync.MAUI.Models
{
    public class SyncOptions
    {

        public SyncOptions(HttpService httpService)
        {
            HttpService = httpService;
        }

        public readonly HttpService HttpService;
        public string LiteDbPath { get; set; } = Path.Combine(FileSystem.AppDataDirectory, "syncData.db");
        public string ApiUrl { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public Func<HttpRequestMessage, Task>? PreRequestAction { get; set; }
        public Func<HttpRequestMessage, Task>? StatusChangeAction { get; set; }

    }
}
