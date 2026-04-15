using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface ICollectionCheckpoint
    {

        string Collection { get;  }

        DateTime? LastUpdatedAt { get;  }

    }
}
