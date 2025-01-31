using MongoDB.Sync.Models;

namespace MongoDB.Sync.Interfaces
{
    public interface ISyncService
    {
        // Event that is triggered when new data is received or updated in real-time
        event EventHandler<UpdatedData>? OnDataUpdated;

        // Starts the synchronization process, optionally from a specific point in time or ID
        Task StartSyncAsync(Func<Task> statusChangeAction);

        // Stops the synchronization process, including disconnecting from SignalR
        Task StopSyncAsync();

        // Allows resuming synchronization, for cases where the network was interrupted
        Task ResumeSyncAsync();

        // Clears the LiteDB cache or performs other clean-up if needed
        Task ClearCacheAsync();
    }
}
