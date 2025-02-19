using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Messages;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;

namespace MongoDB.Sync.LocalDataCache
{

        public class LiveQueryableLiteCollection<T> :  ObservableCollection<T> where T : new()
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<T> _collection;
        private Func<T, bool>? _filter;

        public LiveQueryableLiteCollection(LiteDatabase db, string collectionName, Func<T, bool>? filter = null)
        {
            _db = db;
            _collection = _db.GetCollection<T>(collectionName);
            _filter = filter;

            // Load Initial Data
            ReloadData();

            // Listen for messages instead of LiteDBChangeNotifier
            WeakReferenceMessenger.Default.Register<DatabaseChangeMessage>(this, OnDatabaseChanged);
        }

        private void ReloadData()
        {
            var records = _collection.FindAll().Where(_filter ?? (_ => true)).ToList();
            UpdateCollection(records);
        }

        private void UpdateCollection(List<T> newRecords)
        {
            // Use HashSet for fast lookup
            var currentItems = new HashSet<T>(this);
            var newItems = new HashSet<T>(newRecords);

            // Find removed items
            var toRemove = currentItems.Except(newItems).ToList();
            foreach (var item in toRemove)
                Remove(item);

            // Find new items
            var toAdd = newItems.Except(currentItems).ToList();
            foreach (var item in toAdd)
                Add(item);
        }

        private void OnDatabaseChanged(object recipient, DatabaseChangeMessage message)
        {
            if (message.Value.CollectionName != _collection.Name) return; // Ignore other collections

            var item = BsonMapper.Global.ToObject<T>(message.Value.ChangedItem);

            // Handle deletions
            if (message.Value.IsDeleted)
            {
                var itemToRemove = this.FirstOrDefault(x => x.Equals(item));
                if (itemToRemove != null)
                    Remove(itemToRemove);
                return;
            }

            if (!this.Contains(item))
                    Add(item);
            
        }
    }


}
