using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoDB.Sync.Web.Interfaces
{
    public interface ICriteriaService
    {
        void SetCriteria(string appName, string collectionName, FilterDefinition<BsonDocument> filter);
        FilterDefinition<BsonDocument>? GetCriteria(string appName, string collectionName);
        bool TryGetCriteria(string appName, string collectionName, out FilterDefinition<BsonDocument> filter);

        bool MatchesCriteria(string appName, string collectionName, BsonDocument document);
    }
}
