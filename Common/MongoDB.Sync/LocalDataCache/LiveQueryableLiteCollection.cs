using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using LiteDB;

namespace MongoDB.Sync.LocalDataCache
{


    public class LiveQueryableLiteCollection<T> : ObservableCollection<T> where T : new()
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<T> _collection;
        private Func<T, bool> _filter;

        public LiveQueryableLiteCollection(LiteDatabase db, string collectionName, Func<T, bool> filter = null)
        {
            _db = db;
            _collection = _db.GetCollection<T>(collectionName);
            _filter = filter;

            // Load Initial Data
            ReloadData();

            // Listen for external database changes
            LiteDBChangeNotifier.GetChangeStream(collectionName)
                .Throttle(TimeSpan.FromMilliseconds(100))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(_ => ReloadData());
        }

        public void SetFilter(Func<T, bool> newFilter)
        {
            _filter = newFilter;
            ReloadData();
        }

        private void ReloadData()
        {
            var records = _collection.FindAll().Where(_filter ?? (_ => true)).ToList();
            Clear();
            foreach (var record in records)
                Add(record);
        }
    }

}
