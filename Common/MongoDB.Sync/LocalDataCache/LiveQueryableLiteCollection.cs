using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Core.Services.Models.Models;
using MongoDB.Sync.EventHandlers;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models.Attributes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;

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
        ReloadData();
    }

    public void Refresh() => ReloadData();

    private void ReloadData()
    {
        BeginBatchUpdate();

        var query = _collection.FindAll().AsQueryable();
        if (_filter != null) query = query.Where(_filter).AsQueryable();
        if (_order != null) query = _order(query);

        ReplaceAll(query.ToList());

        EndBatchUpdate();
    }

    private void ReplaceAll(IEnumerable<T> newItems)
    {
        ClearItemsWithoutNotify();

        foreach (var item in newItems)
        {
            base.InsertItem(Count, item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }
    }

    private void ClearItemsWithoutNotify()
    {
        _suspendNotifications = true;
        base.ClearItems();
        _suspendNotifications = false;
    }

    private void ProcessDatabaseChange( DatabaseChangeMessage message)
    {
        if (!_usedCollections.Values.Contains(message.Value.CollectionName)) return;

        BeginBatchUpdate();

        if (message.Value.IsDeleted)
        {
            var toRemove = this.FirstOrDefault(x => x.Id == message.Value.Id);
            if (toRemove != null)
                Remove(toRemove);
            EndBatchUpdate();
            return;
        }

        if (message.Value.CollectionName != _collection.Name)
        {
            HandleLinkedCollectionUpdate(message);
            EndBatchUpdate();
            return;
        }

        var updated = _collection.FindById(new ObjectId(message.Value.Id));
        if (updated == null)
        {
            EndBatchUpdate();
            return;
        }

        var existing = this.FirstOrDefault(x => x.Id == updated.Id);

        if (existing == null)
        {
            if (_filter != null && !_filter(updated)) { EndBatchUpdate(); return; }
            Add(updated);
            EndBatchUpdate();
            return;
        }

        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var newValue = prop.GetValue(updated);
            prop.SetValue(existing, newValue);
        }

        if (_filter != null && !_filter(existing))
        {
            Remove(existing);
            EndBatchUpdate();
            return;
        }

        ItemChanged?.Invoke(this, new ItemChangedEventArgs<T>(ItemChangedEventArgs<T>.CollectionChangeType.Updated, existing));
        EndBatchUpdate();
    }

    private void OnDatabaseChanged(object recipient, DatabaseChangeMessage message)
    {

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                ProcessDatabaseChange(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnDatabaseChanged crash: " + ex);
            }
        });

        
    }

    private void HandleLinkedCollectionUpdate(DatabaseChangeMessage message)
    {
        foreach (var prop in typeof(T).GetProperties())
        {
            var attr = prop.PropertyType.GetCustomAttribute<CollectionNameAttribute>();
            if (attr == null || attr.CollectionName != message.Value.CollectionName) continue;

            var subCol = _db.GetCollection(attr.CollectionName);
            var updatedSub = subCol.FindById(new ObjectId(message.Value.Id));
            var subObj = BsonMapper.Global.Deserialize(prop.PropertyType, updatedSub);

            foreach (var record in this)
            {
                var linkedObj = prop.GetValue(record);
                var linkedIdProp = linkedObj?.GetType().GetProperty("Id");

                if (linkedIdProp?.GetValue(linkedObj) is ObjectId linkedId &&
                    linkedId == message.Value.Id)
                {
                    prop.SetValue(record, subObj);
                    ItemChanged?.Invoke(this, new ItemChangedEventArgs<T>(ItemChangedEventArgs<T>.CollectionChangeType.Updated, record));
                }
            }
        }
    }

    private void BeginBatchUpdate()
    {
        _suspendNotifications = true;
    }

    private void EndBatchUpdate()
    {
        _suspendNotifications = false;
        ReapplySort();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void ReapplySort()
    {
        if (_order == null) return;

        var sorted = _order(this.AsQueryable()).ToList();

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

    protected override void InsertItem(int index, T item)
    {
        if (_suspendNotifications)
        {
            base.InsertItem(index, item);
            return;
        }

        base.InsertItem(index, item);
        ItemChanged?.Invoke(this, new ItemChangedEventArgs<T>(ItemChangedEventArgs<T>.CollectionChangeType.Added, item));
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];

        if (_suspendNotifications)
        {
            Items.RemoveAt(index);
            return;
        }

        base.RemoveItem(index);
        ItemChanged?.Invoke(this, new ItemChangedEventArgs<T>(ItemChangedEventArgs<T>.CollectionChangeType.Removed, item));
    }
}
