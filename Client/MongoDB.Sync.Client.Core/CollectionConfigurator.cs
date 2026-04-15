using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Core
{
    public sealed class CollectionConfigurator
    {
        private readonly SyncRegistry _registry;

        internal CollectionConfigurator(SyncRegistry registry)
        {
            _registry = registry;
        }

        public void AddCollection<T>(string collectionName)
        {
            _registry.Register(typeof(T), collectionName);
        }
    }
}
