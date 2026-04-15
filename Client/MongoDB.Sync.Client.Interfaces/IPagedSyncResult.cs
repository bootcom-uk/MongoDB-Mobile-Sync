using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface IPagedSyncResult
    {
        string CollectionName { get; }
        IReadOnlyCollection<object> Items { get; }
        DateTime? LastSyncDate { get; }
        string? LastSyncedId { get; }
        int PageNumber { get; }
        bool HasMore { get; }


    }
}
