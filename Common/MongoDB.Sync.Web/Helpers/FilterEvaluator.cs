using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace MongoDB.Sync.Web.Helpers
{
    public static class FilterEvaluator
    {
        public static bool FilterDocument(this FilterDefinition<BsonDocument> filter, BsonDocument document)
        {
            var rendered = filter.Render(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry);

            foreach (var element in rendered.Elements)
            {
                if (!document.Contains(element.Name)) return false;

                var value = document[element.Name];
                if (element.Value.IsBsonDocument)
                {
                    var subFilter = element.Value.AsBsonDocument;
                    if (subFilter.Contains("$eq") && !value.Equals(subFilter["$eq"]))
                        return false;

                    if (subFilter.Contains("$in") && !subFilter["$in"].AsBsonArray.Contains(value))
                        return false;

                    if (subFilter.Contains("$ne") && value.Equals(subFilter["$ne"]))
                        return false;

                    // Add more operators as needed...
                }
                else if (!value.Equals(element.Value))
                {
                    return false;
                }
            }

            return true;
        }
    }

}
