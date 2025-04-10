﻿using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;

namespace MongoDB.Sync.LocalDataCache
{

    public class LiveQueryableLiteCollection<T> : ObservableCollection<T> where T : BaseLocalCacheModel
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<T> _collection;
        private Func<T, bool>? _filter;
        private Func<IQueryable<T>, IOrderedQueryable<T>>? _order;
        private bool _suspendNotifications = false;
        private Dictionary<Type, string> _usedCollections = new();

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

            _usedCollections.Add(typeof(T), collectionName);

            foreach (var prpInfo in typeof(T).GetProperties())
            {
                var attribute = prpInfo.PropertyType.GetCustomAttribute<CollectionNameAttribute>();
                if (attribute is null) continue;
                _usedCollections.Add(prpInfo.PropertyType, attribute.CollectionName);
            }

            // Load Initial Data
            ReloadData();

            // Listen for messages instead of LiteDBChangeNotifier
            WeakReferenceMessenger.Default.Register<DatabaseChangeMessage>(this, OnDatabaseChanged);
        }

        private void ReloadData()
        {

            BeginUpdate();

            IQueryable<T> query = _collection.FindAll().AsQueryable();

            if (_filter != null)
            {
                query = query.Where(_filter).AsQueryable();
            }

            if (_order != null)
            {
                query = _order(query);
            }

            var mapper = _collection.EntityMapper;
            var records = query.ToList();

            Clear();
            foreach (var record in records)
            {
                Add(record);
            }

            EndUpdate();
        }

        private void OnDatabaseChanged(object recipient, DatabaseChangeMessage message)
        {
            if (!_usedCollections.Values.Contains(message.Value.CollectionName)) return;

            if (message.Value.IsDeleted)
            {
                var itemToRemove = this.FirstOrDefault(x => x.Id == message.Value.Id);
                if (itemToRemove != null)
                    Remove(itemToRemove);
                return;
            }


            BeginBatchUpdate();

            // We've updated a collection which isn't the current collection but a linked item
            // so we need to up
            if (message.Value.CollectionName != _collection.Name)
            {
                foreach (var item in typeof(T).GetProperties())
                {
                    var collectionName = item.PropertyType.GetCustomAttribute<CollectionNameAttribute>();
                    if (collectionName is null) continue;
                    if (collectionName.CollectionName != message.Value.CollectionName) continue;

                    var subCollection = _db.GetCollection(collectionName.CollectionName);
                    var newRecordValue = subCollection.FindById(new ObjectId(message.Value.Id));
                    var newRecord = BsonMapper.Global.Deserialize(item.PropertyType, newRecordValue);

                    foreach (var record in this)
                    {
                        var subRecord = record.GetType().GetProperty(item.Name)!.GetValue(record);
                        var id = subRecord?.GetType().GetProperty("Id");
                        if (id is not null && (ObjectId?) id.GetValue(subRecord) == message.Value.Id)
                        {                            

                            record.GetType().GetProperty(item.Name)!.SetValue(record, newRecord);
                        }
                    }

                }
            }

                // If its the current collection that has been updated then we need to update the item
                if (message.Value.CollectionName == _collection.Name)
            {
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
            }
            

            EndBatchUpdate();

        }

        private void ReapplySort()
        {
            if (_order == null) return;

            var sorted = _order(this.AsQueryable()).ToList();

            // Check if the current order is already the same as the sorted order 
            // If so, we don't need to do anything
            if (this.Select(x => x.Id).SequenceEqual(sorted.Select(x => x.Id))) return;

            for (int targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
            {
                var item = sorted[targetIndex];
                var currentIndex = IndexOf(item);

                if (currentIndex != targetIndex && currentIndex >= 0)
                {
                    Move(currentIndex, targetIndex);
                }
            }
        }

        public void BeginUpdate()
        {
            _suspendNotifications = true;
        }

        public void EndUpdate()
        {
            _suspendNotifications =false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void InsertItem(int index, T item)
        {
            if (_suspendNotifications)
            {
                base.InsertItem(index, item);
                return;
            }
            
            base.InsertItem(index, item);
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suspendNotifications)
                return;
            base.OnCollectionChanged(e);
        }

        protected override void ClearItems()
        {
            if (_suspendNotifications)
            {
                Items.Clear();
                return;
            }

            base.ClearItems();
        }

        public void BeginBatchUpdate()
        {
            _suspendNotifications = true;
        }

        public void EndBatchUpdate()
        {
            _suspendNotifications = false;
            ReapplySort(); // Apply any sorting now that the batch is done
            try
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move));                
            }
            catch (Exception ex)
            {
                // Handle exception
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }                
        }
    }
}
