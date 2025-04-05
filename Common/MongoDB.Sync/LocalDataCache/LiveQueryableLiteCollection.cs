using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json;

namespace MongoDB.Sync.LocalDataCache
{

    public class LiveQueryableLiteCollection<T> : ObservableCollection<T> where T : BaseLocalCacheModel
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<T> _collection;
        private Func<T, bool>? _filter;
        private Func<IQueryable<T>, IOrderedQueryable<T>>? _order;

        private static readonly HashSet<Type> _mappedTypes = new();

        public LiveQueryableLiteCollection(
            LiteDatabase db,
            string collectionName,
            Func<T, bool>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? order = null)
        {
            _db = db;
            _collection = _db.GetCollection<T>(collectionName);
            _filter = filter;
            _order = order;

            // Load Initial Data
            ReloadData();

            // Listen for messages instead of LiteDBChangeNotifier
            WeakReferenceMessenger.Default.Register<DatabaseChangeMessage>(this, OnDatabaseChanged);
        }

        private void ReloadData()
        {
            IQueryable<T> query = _collection.FindAll().AsQueryable();

            if (_filter != null)
            {
                query = query.Where(_filter).AsQueryable();
            }

            if (_order != null)
            {
                query = _order(query);
            }

            var records = query.ToList();

            Clear();
            foreach (var record in records)
            {
                Add(record);
            }
        }

        private void OnDatabaseChanged(object recipient, DatabaseChangeMessage message)
        {
            if (message.Value.CollectionName != _collection.Name) return;

            if (message.Value.IsDeleted)
            {
                var itemToRemove = this.FirstOrDefault(x => x.Id == message.Value.Id);
                if (itemToRemove != null)
                    Remove(itemToRemove);
                return;
            }

            var existingItem = this.FirstOrDefault(x => x.Id == message.Value.Id);
            var record = _collection.FindById(new ObjectId(message.Value.Id));

            var tmpList = new List<T>() { record };

            if (existingItem is null)
            {
                if (_filter != null && tmpList.Where(_filter).Count() == 0) return;

                Add(record);
                return;
            }

            foreach (var prpInfo in typeof(T).GetProperties())
            {
                var value = prpInfo.GetValue(record);
                prpInfo.SetValue(existingItem, value);
            }

            if (_filter != null && tmpList.Where(_filter).Count() == 0)
            {
                Remove(record);
                return;
            }

            ReapplySort();
        }

        private void ReapplySort()
        {
            if (_order != null)
            {
                var sortedList = _order(this.AsQueryable()).ToList();

                Clear();
                foreach (var item in sortedList)
                {
                    Add(item);
                }
            }
        }

    }



}
