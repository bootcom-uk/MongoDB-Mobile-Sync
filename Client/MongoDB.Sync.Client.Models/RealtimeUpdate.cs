using MongoDB.Sync.Client.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using static MongoDB.Sync.Client.Interfaces.IRealtimeUpdate;

namespace MongoDB.Sync.Client.Models
{
    public sealed class RealtimeUpdate : IRealtimeUpdate
    {
        public string CollectionName { get; init; } = default!;
        public object Entity { get; init; } = default!;
        public SyncOperation Operation { get; init; }
        public DateTime? Timestamp { get; init; }
    }
}
