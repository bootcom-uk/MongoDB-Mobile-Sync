using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using Microsoft.Extensions.Logging;
using MongoDB.Sync.Core.Services.Models.Models;
using MongoDB.Sync.Core.Services.Models.Services;
using MongoDB.Sync.LocalDataCache;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;
using Services;
using System.Collections;
using System.Data;
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
        private readonly IMessenger _messenger;
        private readonly BaseTypeResolverService _baseTypeResolverService;

        public LocalCacheService(IMessenger messenger, LocalDatabaseSyncService localDatabaseSyncService, ILogger<LocalCacheService> logger, HttpService httpService, string apiUrl, string appName, Func<HttpRequestMessage, Task>? preRequestAction, Func<HttpRequestMessage, Task>? statusChangeAction, BaseTypeResolverService baseTypeResolverService)
        {
            _logger = logger;
            _httpService = httpService;
            _apiUrl = apiUrl;
            _appName = appName;
            _preRequestAction = preRequestAction;
            _statusChangeAction = statusChangeAction;
            _messenger = messenger; 
            _db = localDatabaseSyncService.LiteDb;
            _baseTypeResolverService = baseTypeResolverService;

            InitializeReferenceResolvers();

            Task.Run(ProcessQueue);
        }


        private void InitializeReferenceResolvers()
        {
            var mapper = BsonMapper.Global;

           
            foreach(var type in _baseTypeResolverService.MappedTypes.Keys)
            {
                mapper.RegisterType(type, 
                   deserialize: bson => {

                       if(bson is null) return null;

                       var mappedType = _baseTypeResolverService.MappedTypes[type];

                        var item = Activator.CreateInstance(type);

                        foreach (var kvp in (BsonDocument)bson)
                        {
                            var key = kvp.Key;
                           if (key == "_id") key = "Id";
                            var prpInfo = mappedType.FirstOrDefault(p => p.Name == key);
                           if (prpInfo is not null)
                           {

                               if (kvp.Value != null && kvp.Value.IsDocument && kvp.Value.AsDocument.Keys.Count == 1)
                               {
                                   var id = kvp.Value.AsDocument.First();
                                   if (id.Value.IsString)
                                   {
                                       var collectionName = prpInfo.PropertyType.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;

                                       var bsonObject = _db.GetCollection(collectionName).FindById(new ObjectId(id.Value.AsString));
                                       var displayObject = BsonMapper.Global.Deserialize(prpInfo.PropertyType, bsonObject);

                                       item!.GetType().GetProperty(key)?.SetValue(item, displayObject);
                                       continue;
                                   }
                               }

                               if (kvp.Value != null && kvp.Value.IsDocument)
                               {
                                   var prpName = prpInfo.Name;
                               }

                                   if (kvp.Value != null && kvp.Value.IsObjectId)
                                {
                                    var collectionName = prpInfo.PropertyType.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;
                                    var bsonObject = _db.GetCollection(collectionName).FindById(kvp.Value.AsObjectId);
                                    var displayObject = BsonMapper.Global.Deserialize(prpInfo.PropertyType, bsonObject);

                                    item!.GetType().GetProperty(key)?.SetValue(item, displayObject);
                                   continue;
                               }                                
                            }
                           

                            var propertyValue = kvp.Value;

                           if (kvp.Value != null && kvp.Value.IsDocument)
                           {
                               var outputPropertyType = item!.GetType().GetProperty(kvp.Key)?.PropertyType;
                               if(outputPropertyType is not null)
                               {
                                   var displayObject = BsonMapper.Global.Deserialize(outputPropertyType, kvp.Value);
                                   item.GetType().GetProperty(key)?.SetValue(item, displayObject);
                               }
                               continue;
                           }

                           if (kvp.Value!.IsBoolean)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsBoolean);
                                continue;
                            }

                            if (kvp.Value!.IsDateTime)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsDateTime);
                                continue;
                            }

                           var propertyInfo = item.GetType().GetProperty(key);
                           if (propertyInfo != null &&
                               propertyInfo.PropertyType == typeof(ObjectId) &&
                               kvp.Value != null &&
                               kvp.Value.IsString)
                           {
                               propertyInfo.SetValue(item, new ObjectId(kvp.Value.AsString));
                               continue;
                           }

                           if (propertyInfo != null &&
                              propertyInfo.PropertyType != typeof(string) &&
                              kvp.Value != null &&
                              kvp.Value.IsString && !_baseTypeResolverService.MappedTypes.ContainsKey(propertyInfo.PropertyType)) 
                           {
                               propertyInfo.SetValue(item, Convert.ChangeType(kvp.Value.AsString, propertyInfo.PropertyType));
                               continue;
                           }

                           if (kvp.Value!.IsString)
                            {

                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsString);
                                continue;
                            }

                            if (kvp.Value!.IsInt32)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsInt32);
                                continue;
                            }

                            if (kvp.Value.IsInt64)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsInt64);
                                continue;
                            }

                            if (kvp.Value.IsDouble)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsDouble);
                                continue;
                            }

                            if (kvp.Value.IsDecimal)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsDecimal);
                                continue;
                            }

                            if (kvp.Value.IsGuid)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsGuid);
                                continue;
                            }

                            if (kvp.Value.IsObjectId)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsObjectId);
                                continue;
                            }

                            if (kvp.Value.IsArray)
                            {

                               if (prpInfo?.PropertyType is null || prpInfo?.PropertyType?.GenericTypeArguments is null || prpInfo?.PropertyType?.GenericTypeArguments.Count() == 0) continue;

                                var listType = typeof(List<>).MakeGenericType(prpInfo?.PropertyType?.GenericTypeArguments[0]!);

                               if(listType is null)
                               {
                                   continue;
                               }

                               var list = (IList?)Activator.CreateInstance(listType);
                               if(list is null) continue;

                               foreach (var itemValue in kvp.Value.AsArray)
                                {
                                    var collectionName = prpInfo!.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;
                                    var displayObject = _db.GetCollection(collectionName).FindById(itemValue.ToString());
                                    list.Add(displayObject);
                                }
                                item!.GetType().GetProperty(key)?.SetValue(item, list);
                                continue;
                            }

                            if (kvp.Value.IsObjectId)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value.AsObjectId);
                                continue;
                            }

                            if (kvp.Value.IsNull)
                            {
                                item!.GetType().GetProperty(key)?.SetValue(item, null);
                                continue;
                            }

                            item!.GetType().GetProperty(key)?.SetValue(item, kvp.Value);
                        }

                        return item;
                    }, 
                    serialize :(item) => {
                        var document = new BsonDocument();

                        foreach (var prpInfo in item.GetType().GetProperties())
                        {

                            if (prpInfo.Name == "Id")
                            {
                                document.Add("_id", BsonMapper.Global.Serialize(prpInfo.GetValue(item)));
                                continue;
                            }

                            if (typeof(BaseLocalCacheModel).IsAssignableFrom(prpInfo.PropertyType))
                            {                              
                                var underlyingObject = (BaseLocalCacheModel?) prpInfo.GetValue(item);
                                if (underlyingObject is null) continue;
                                document.Add(prpInfo.Name, underlyingObject.Id);
                                continue;
                            }
                            document.Add(prpInfo.Name, BsonMapper.Global.Serialize(prpInfo.GetValue(item)));
                        }

                        return document;
                    });
            }
            
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

        public LiveQueryableLiteCollection<T> GetLiveCollection<T>(string name, Func<T, bool>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? order = null) where T : BaseLocalCacheModel
        {
            return new LiveQueryableLiteCollection<T>(_messenger, _db, name, filter, order);
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
                if (localCacheDataChange.Document != null)
                {
                    string jsonString = LiteDB.JsonSerializer.Serialize(localCacheDataChange.Document);

                    localCacheDataChange.SerializedDocument = jsonString;
                }

                var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/Send/{_appName}"), HttpMethod.Post);

                if(_preRequestAction != null)
                {
                    builder.PreRequest(_preRequestAction);
                }

                if(_statusChangeAction != null)
                {
                    builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusChangeAction);
                }

                builder.WithJsonContent<LocalCacheDataChange>(localCacheDataChange);
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

            // If a removal then clear from the local cache
            if(localCacheDataChange.IsDeletion)
            {
                var primaryCollection = _db.GetCollection<BsonDocument>(localCacheDataChange.CollectionName);
                primaryCollection.Delete(new ObjectId(localCacheDataChange.Id));
            }

            _messenger.Send(new DatabaseChangeMessage(new DatabaseChangeParameters()
            {
                ChangedItem = localCacheDataChange.Document,
                IsDeleted = localCacheDataChange.IsDeletion,
                CollectionName = localCacheDataChange.CollectionName,
                Id = new ObjectId(localCacheDataChange.Id)
            }));

            var collection = _db.GetCollection<SyncLocalCacheDataChange>(collectionNameAttribute.CollectionName);   

            collection.Upsert(localCacheDataChange);
            _changesToProcess.Enqueue(localCacheDataChange);
        }

        public void Save<T>(T item) where T : BaseLocalCacheModel
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            var attribute = item.GetType().GetCustomAttribute<CollectionNameAttribute>();
            if(attribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");
            var collection = _db.GetCollection(attribute.CollectionName);
            var idValue = item.Id;
            if (idValue is null) return;

            var bson = BsonMapper.Global.Serialize(typeof(T), item).AsDocument;
            collection.Upsert(bson);

            Enqueue(new SyncLocalCacheDataChange
            {
                CollectionName = attribute.CollectionName,
                IsDeletion = false,
                Id = idValue.ToString(),
                Document = bson
            });

        }

        public void Delete<T>(T item) where T : BaseLocalCacheModel
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            var attribute = item.GetType().GetCustomAttribute<CollectionNameAttribute>();
            if (attribute is null) throw new InvalidOperationException("CollectionNameAttribute is missing");
            var collection = _db.GetCollection<T>(attribute.CollectionName);
            var idValue = item.Id;
            if (idValue is null) return;

            // Does this record already exist in the collection? 
            var record = collection.FindById(idValue);

            // Record doesn't exist so exit
            if (record is null) return;

            collection.Delete(idValue);

            Enqueue(new SyncLocalCacheDataChange
            {
                CollectionName = attribute.CollectionName,
                IsDeletion = true,
                Id = idValue.ToString()
            });
        }


    }
}
