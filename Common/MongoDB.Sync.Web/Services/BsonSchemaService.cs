using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoDB.Sync.Web.Services
{
    public class BsonSchemaService
    {

        private readonly IMongoDatabase _database;

        public BsonSchemaService(IMongoDatabase database)
        {
            _database = database;
        }

        public async Task<Dictionary<string, object>> GetClusterSchemaAsync(string collectionName, int sampleSize = 100)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var documents = await collection.Find(new BsonDocument()).Limit(sampleSize).ToListAsync();

            return BuildSchema(documents);
        }

        private Dictionary<string, object> BuildSchema(List<BsonDocument> documents)
        {
            var schema = new Dictionary<string, object>();

            foreach (var document in documents)
            {
                foreach (var field in document.Elements)
                {
                    schema[field.Name] = GetBsonType(field.Value);
                }
            }

            return schema;
        }

        private object GetBsonType(BsonValue value)
        {
            if (value.IsBsonDocument)
            {
                return GetObjectSchema(value.AsBsonDocument);
            }
            else if (value.IsBsonArray)
            {
                return GetArrayType(value.AsBsonArray);
            }
            else
            {
                return value.BsonType.ToString();
            }
        }

        private Dictionary<string, object> GetObjectSchema(BsonDocument document)
        {
            var nestedSchema = new Dictionary<string, object>();
            foreach (var field in document.Elements)
            {
                nestedSchema[field.Name] = GetBsonType(field.Value);
            }
            return nestedSchema;
        }

        private object GetArrayType(BsonArray array)
        {
            if (!array.Any()) return "Array<Unknown>";

            var elementTypes = new HashSet<string>();
            foreach (var item in array)
            {
                elementTypes.Add(GetBsonType(item).ToString());
            }

            return elementTypes.Count == 1
                ? $"Array<{elementTypes.First()}>"
                : $"Array<Mixed({string.Join(", ", elementTypes)})>";
        }

    }
}
