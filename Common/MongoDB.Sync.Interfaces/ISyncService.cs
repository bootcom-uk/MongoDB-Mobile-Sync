
namespace MongoDB.Sync.Interfaces
{
    public interface ISyncService
    {

        bool SyncIsStarting { get; set; }

        bool SyncHasCompleted { get; set; }

        // Indicates if the synchronization process is currently in progress
        bool AppSyncInProgress { get; set; }

        // Starts the synchronization process, optionally from a specific point in time or ID
        Task StartSyncAsync();

        // Stops the synchronization process, including disconnecting from SignalR
        Task StopSyncAsync();

        // Allows resuming synchronization, for cases where the network was interrupted
        Task ResumeSyncAsync();

        // Clears the LiteDB cache or performs other clean-up if needed
        Task ClearCacheAsync();

    }
}
