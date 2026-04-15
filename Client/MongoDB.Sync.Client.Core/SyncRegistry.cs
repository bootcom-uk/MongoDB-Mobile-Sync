using MongoDB.Sync.Client.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Core
{
    public sealed class SyncRegistry
    {
        private readonly Dictionary<string, SyncCollectionDefinition> _collections = new(StringComparer.OrdinalIgnoreCase);

        public void Register(Type modelType, string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentException("Collection name cannot be empty.");

            if (_collections.ContainsKey(collectionName))
                throw new InvalidOperationException(
                    $"Collection '{collectionName}' is already registered.");

            _collections[collectionName] = new SyncCollectionDefinition(modelType, collectionName);
        }

        public IReadOnlyCollection<SyncCollectionDefinition> GetAll() => _collections.Values.ToList().AsReadOnly();

        public SyncCollectionDefinition? Get(string collectionName)
        {
            _collections.TryGetValue(collectionName, out var value);
            return value;
        }
    }
}
