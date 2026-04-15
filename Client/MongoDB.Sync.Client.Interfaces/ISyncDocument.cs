using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface ISyncDocument
    {
        string Id { get; }
        string Collection { get; }
        string Json { get; }
        DateTime UpdatedAt { get; }
        bool IsDeleted { get; }
    }
}
