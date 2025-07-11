﻿using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Core.Services.Models.Models;
using MongoDB.Sync.EventHandlers;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models.Attributes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Threading.Tasks;

namespace MongoDB.Sync.LocalDataCache;

public class LiveQueryableLiteCollection<T> : ObservableCollection<T> where T : BaseLocalCacheModel
{
    public event EventHandler<ItemChangedEventArgs<T>>? ItemChanged;

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<T> _collection;
    private readonly IMessenger _messenger;
    private readonly Func<T, bool>? _filter;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>>? _order;

    private bool _suspendNotifications = false;
    private readonly Dictionary<Type, string> _usedCollections = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public LiveQueryableLiteCollection(
        IMessenger messenger,
        LiteDatabase db,
        string collectionName,
        Func<T, bool>? filter = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? order = null)
    {
        _db = db;
        _collection = _db.GetCollection<T>(collectionName);
        _filter = filter;
        _order = order;
        _messenger = messenger;

        _usedCollections.TryAdd(typeof(T), collectionName);

        foreach (var prpInfo in typeof(T).GetProperties())
        {
            var attr = prpInfo.PropertyType.GetCustomAttribute<CollectionNameAttribute>();
            if (attr != null)
                _usedCollections.TryAdd(prpInfo.PropertyType, attr.CollectionName);
        }

        _messenger.Register<DatabaseChangeMessage>(this, OnDatabaseChanged);

        _ = RefreshAsync(); // Fire and forget
    }

    public Task RefreshAsync() => ReloadDataAsync();

    private async Task ReloadDataAsync()
    {
        await _refreshLock.WaitAsync();

        try
        {
            var query = _collection.FindAll().AsQueryable();
            if (_filter != null) query = query.Where(_filter).AsQueryable();
            if (_order != null) query = _order(query);

            var items = query.ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SuspendNotifications(() =>
                {
                    ClearItems();
                    foreach (var item in items)
                        Add(item);
                });

                RaiseReset();
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void SuspendNotifications(Action action)
    {
        _suspendNotifications = true;
        try { action(); }
        finally { _suspendNotifications = false; }
    }

    private void RaiseReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnDatabaseChanged(object recipient, DatabaseChangeMessage message)
    {
        // Queue to background, then post result to UI thread in batch
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessDatabaseChangeAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Database change processing failed: " + ex);
            }
        });
    }

    private async Task ProcessDatabaseChangeAsync(DatabaseChangeMessage message)
    {
        if (!_usedCollections.Values.Contains(message.Value.CollectionName)) return;

        if (message.Value.IsDeleted)
        {
            var itemToRemove = this.FirstOrDefault(x => x.Id == message.Value.Id);
            if (itemToRemove != null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Remove(itemToRemove);
                    ItemChanged?.Invoke(this, new(ItemChangedEventArgs<T>.CollectionChangeType.Removed, itemToRemove));
                });
            }
            return;
        }

        if (message.Value.CollectionName != _collection.Name)
        {
            HandleLinkedCollectionUpdate(message);
            return;
        }

        var updated = _collection.FindById(new ObjectId(message.Value.Id));
        if (updated == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var existing = this.FirstOrDefault(x => x.Id == updated.Id);
            if (existing == null)
            {
                if (_filter != null && !_filter(updated)) return;
                Add(updated);
                ItemChanged?.Invoke(this, new(ItemChangedEventArgs<T>.CollectionChangeType.Added, updated));
            }
            else
            {
                foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var newVal = prop.GetValue(updated);
                    prop.SetValue(existing, newVal);
                }

                if (_filter != null && !_filter(existing))
                {
                    Remove(existing);
                    ItemChanged?.Invoke(this, new(ItemChangedEventArgs<T>.CollectionChangeType.Removed, existing));
                    return;
                }

                ItemChanged?.Invoke(this, new(ItemChangedEventArgs<T>.CollectionChangeType.Updated, existing));
            }
        });
    }

    private void HandleLinkedCollectionUpdate(DatabaseChangeMessage message)
    {
        foreach (var prop in typeof(T).GetProperties())
        {
            var attr = prop.PropertyType.GetCustomAttribute<CollectionNameAttribute>();
            if (attr?.CollectionName != message.Value.CollectionName) continue;

            var subCol = _db.GetCollection(attr.CollectionName);
            var updatedSub = subCol.FindById(new ObjectId(message.Value.Id));
            var subObj = BsonMapper.Global.Deserialize(prop.PropertyType, updatedSub);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var record in this)
                {
                    var linkedObj = prop.GetValue(record);
                    var linkedId = linkedObj?.GetType().GetProperty("Id")?.GetValue(linkedObj);

                    if (linkedId?.Equals(message.Value.Id) == true)
                    {
                        prop.SetValue(record, subObj);
                        ItemChanged?.Invoke(this, new(ItemChangedEventArgs<T>.CollectionChangeType.Updated, record));
                    }
                }
            });
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suspendNotifications) return;
        base.OnCollectionChanged(e);
    }
}
