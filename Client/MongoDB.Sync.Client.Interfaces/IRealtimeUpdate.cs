using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface IRealtimeUpdate
    {

        public enum SyncOperation
        {
            Insert,
            Update,
            Delete
        }

        string CollectionName { get; }
        object Entity { get; }
        SyncOperation Operation { get; }
        DateTime? Timestamp { get; }
    }
}
