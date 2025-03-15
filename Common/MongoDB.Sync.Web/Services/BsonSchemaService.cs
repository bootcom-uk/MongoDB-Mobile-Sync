using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Web.Services
{
    public class BsonSchemaService
    {
        private readonly IMongoClient _client;

        public BsonSchemaService(IMongoClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Gets the full schema: all databases, collections, and field structures
        /// </summary>
        public async Task<IEnumerable<DatabaseSchema>> GetFullDatabaseSchemaAsync(int sampleSize = 100)
        {
            var databases = await _client.ListDatabaseNamesAsync();
            var databaseList = await databases.ToListAsync();

            var schemaTasks = databaseList.Select(async dbName =>
            {
                var database = _client.GetDatabase(dbName);
                var collections = await database.ListCollectionNamesAsync();
                var collectionList = await collections.ToListAsync();

                var collectionSchemas = await Task.WhenAll(collectionList.Select(async collectionName =>
                {
                    var collection = database.GetCollection<BsonDocument>(collectionName);
                    var documents = await collection.Find(new BsonDocument()).Limit(sampleSize).ToListAsync();

                    return new CollectionSchema
                    {
                        CollectionName = collectionName,
                        Fields = BuildSchema(documents)
                    };
                }));

                return new DatabaseSchema
                {
                    DatabaseName = dbName,
                    Collections = collectionSchemas.ToList()
                };
            });

            return await Task.WhenAll(schemaTasks);
        }

        /// <summary>
        /// Builds schema from documents
        /// </summary>
        private List<FieldSchema> BuildSchema(List<BsonDocument> documents)
        {
            var schema = new Dictionary<string, FieldSchema>();

            foreach (var document in documents)
            {
                foreach (var field in document.Elements)
                {
                    if (!schema.ContainsKey(field.Name))
                    {
                        schema[field.Name] = new FieldSchema
                        {
                            Name = field.Name,
                            Type = GetBsonType(field.Value)
                        };
                    }
                }
            }

            return schema.Values.ToList();
        }

        /// <summary>
        /// Determines BSON type (handles objects and arrays)
        /// </summary>
        private FieldType GetBsonType(BsonValue value)
        {
            if (value.IsBsonDocument)
            {
                return new FieldType
                {
                    Type = "Object",
                    NestedFields = GetObjectSchema(value.AsBsonDocument)
                };
            }
            else if (value.IsBsonArray)
            {
                return GetArrayType(value.AsBsonArray);
            }
            else
            {
                return new FieldType { Type = value.BsonType.ToString() };
            }
        }

        /// <summary>
        /// Extracts object schema
        /// </summary>
        private List<FieldSchema> GetObjectSchema(BsonDocument document)
        {
            return document.Elements
                .Select(field => new FieldSchema
                {
                    Name = field.Name,
                    Type = GetBsonType(field.Value)
                })
                .ToList();
        }

        /// <summary>
        /// Extracts array type
        /// </summary>
        private FieldType GetArrayType(BsonArray array)
        {
            if (!array.Any())
                return new FieldType { Type = "Array<Unknown>" };

            var elementTypes = new HashSet<string>();
            foreach (var item in array)
            {
                elementTypes.Add(GetBsonType(item).Type);
            }

            return new FieldType
            {
                Type = elementTypes.Count == 1
                    ? $"Array<{elementTypes.First()}>"
                    : $"Array<Mixed({string.Join(", ", elementTypes)})>"
            };
        }
    }
}
