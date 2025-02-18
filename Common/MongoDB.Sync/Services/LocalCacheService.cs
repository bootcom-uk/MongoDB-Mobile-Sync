using LiteDB;
using Microsoft.Extensions.Logging;
using MongoDB.Sync.LocalDataCache;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;
using Services;
using System.Reflection;

namespace MongoDB.Sync.Services
{
    public class LocalCacheService
    {

        private readonly LiteDatabase _db;
        private readonly ILogger<LocalCacheService> _logger;
        private readonly HttpService _httpService;
        private readonly string _apiUrl;
        private readonly string _appName;
        private readonly Func<HttpRequestMessage, Task>? _preRequestAction;
        private readonly Func<HttpRequestMessage, Task>? _statusChangeAction;

        public LocalCacheService(LocalDatabaseSyncService localDatabaseSyncService, ILogger<LocalCacheService> logger, HttpService httpService, string apiUrl, string appName, Func<HttpRequestMessage, Task>? preRequestAction, Func<HttpRequestMessage, Task>? statusChangeAction)
        {
            _logger = logger;
            _httpService = httpService;
            _apiUrl = apiUrl;
            _appName = appName;
            _preRequestAction = preRequestAction;
            _statusChangeAction = statusChangeAction;

            // Define LiteDB file location in MAUI            
            _db = localDatabaseSyncService.LiteDb;

            Task.Run(ProcessQueue);
        }

        public string GetCollectionName<T>() where T : new()
        {
            var attribute = typeof(T).GetCustomAttribute<CollectionNameAttribute>();
            if (attribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");
            return attribute.CollectionName;
        }

        public ILiteCollection<T> GetCollection<T>(string name) where T : new()
        {
            return _db.GetCollection<T>(name);
        }

        public LiveQueryableLiteCollection<T> GetLiveCollection<T>(string name, Func<T, bool> filter = null) where T : new()
        {
            return new LiveQueryableLiteCollection<T>(_db, name, filter);
        }

        private ObjectId? GetId<T>(T item)
        {
            var idValue = (null as ObjectId);

            foreach(var fldInfo in item.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var databaseFieldNameAttribute = fldInfo.GetCustomAttribute<DatabaseFieldNameAttribute>();
                if (databaseFieldNameAttribute is null) continue;
                if (databaseFieldNameAttribute.DatabaseFieldName == "_id")
                {
                    return fldInfo.GetValue(item) as ObjectId;
                }
            }

            foreach (var prop in item.GetType().GetProperties())
            {
                if (prop.Name == "_id")
                {
                    return prop.GetValue(item) as ObjectId;
                }

                var databaseFieldNameAttribute = prop.GetCustomAttribute<DatabaseFieldNameAttribute>();
                if (databaseFieldNameAttribute is null) continue;
                if (databaseFieldNameAttribute.DatabaseFieldName == "_id")
                {
                    return prop.GetValue(item) as ObjectId;
                }
            }
            return idValue;
        }

        private Queue<SyncLocalCacheDataChange> _changesToProcess = new();

        private async Task ProcessQueue()
        {

            // When starting up we need to load any changes that haven't been processed
            var collectionNameAttribute = typeof(SyncLocalCacheDataChange).GetCustomAttribute<CollectionNameAttribute>();
            if (collectionNameAttribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");

            var collection = _db.GetCollection<SyncLocalCacheDataChange>(collectionNameAttribute.CollectionName);

            var records = collection.FindAll().OrderBy(record => record.Timestamp);
            foreach (var record in records)
            {
                _changesToProcess.Enqueue(record);
            }

            while (true)
            {
                // No changes to process so wait for a bit
                if (_changesToProcess.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    continue;
                }
                
                var localCacheDataChange = _changesToProcess.Peek();

                var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/Send/{_appName}"), HttpMethod.Post);

                if(_preRequestAction != null)
                {
                    builder.PreRequest(_preRequestAction);
                }

                if(_statusChangeAction != null)
                {
                    builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusChangeAction);
                }

                builder.WithJsonContent<SyncLocalCacheDataChange>(localCacheDataChange);
                builder.WithRetry(3);

                var response = await builder.SendAsync();

                if (response.Success)
                {
                    // Clear out from the local cache
                    collection.DeleteMany(record => record.InternalId == localCacheDataChange.InternalId && record.Id == localCacheDataChange.Id);

                    // Successfully processed the change so remove it from the queue
                    _changesToProcess.Dequeue();
                }

            }
        }

        private void Enqueue(SyncLocalCacheDataChange localCacheDataChange)
        {
            var collectionNameAttribute = localCacheDataChange.GetType().GetCustomAttribute<CollectionNameAttribute>();
            if (collectionNameAttribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");

            var collection = _db.GetCollection<SyncLocalCacheDataChange>(collectionNameAttribute.CollectionName);   

            collection.Upsert(localCacheDataChange);
            _changesToProcess.Enqueue(localCacheDataChange);
        }

        public void Save<T>(T item) 
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            var attribute = item.GetType().GetCustomAttribute<CollectionNameAttribute>();
            if(attribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");
            var collection = _db.GetCollection<T>(attribute.CollectionName);
            var idValue = GetId(item);
            if (idValue is null) return;

            Enqueue(new SyncLocalCacheDataChange
            {
                CollectionName = attribute.CollectionName,
                IsDeletion = false,
                Id = idValue.ToString(),
                Document = BsonMapper.Global.ToDocument(item)
            });

        }

        public void Delete<T>(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            var attribute = item.GetType().GetCustomAttribute<CollectionNameAttribute>();
            if (attribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");
            var collection = _db.GetCollection<T>(attribute.CollectionName);
            var idValue = GetId(item);
            if (idValue is null) return;

            // Does this record already exist in the collection? 
            var record = collection.FindById(idValue);

            // Record doesn't exist so exit
            if (record is null) return;

            Enqueue(new SyncLocalCacheDataChange
            {
                CollectionName = attribute.CollectionName,
                IsDeletion = true,
                Id = idValue.ToString()
            });
        }


    }
}
