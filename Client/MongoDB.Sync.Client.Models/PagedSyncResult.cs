using MongoDB.Sync.Client.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Models
{
    public sealed class PagedSyncResult : IPagedSyncResult
    {
        public string CollectionName { get; init; } = default!;
        public IReadOnlyCollection<object> Items { get; init; } = Array.Empty<object>();
        public DateTime? MaxTimestamp { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
        public bool HasMore { get; init; }

        public DateTime? LastSyncDate => throw new NotImplementedException();

        public string? LastSyncedId => throw new NotImplementedException();

        public int PageNumber => throw new NotImplementedException();
    }
}
