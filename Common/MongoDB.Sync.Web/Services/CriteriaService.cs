using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Sync.Web.Helpers;
using MongoDB.Sync.Web.Models;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

namespace MongoDB.Sync.Web.Services
{


    public class CriteriaService
    {
        private readonly Dictionary<string, object> _tokenValues;

        public CriteriaService(Dictionary<string, object> tokenValues)
        {
            _tokenValues = tokenValues;
        }

        public CriteriaResult<BsonDocument> Build(string? rawCriteria)
        {
            // If no criteria provided, everything passes
            if (string.IsNullOrWhiteSpace(rawCriteria))
            {
                return new CriteriaResult<BsonDocument>
                {
                    Predicate = _ => true,
                    MongoFilter = Builders<BsonDocument>.Filter.Empty
                };
            }

            // Replace @tokens with actual values
            var resolved = ReplaceTokens(rawCriteria);

            // Compile to expression using Dynamic LINQ
            var lambda = DynamicExpressionParser.ParseLambda<BsonDocument, bool>(
                new ParsingConfig
                {
                    CustomTypeProvider = new CriteriaTypeProvider()
                },
                false,
                resolved
            );

            // Build MongoDB filter (partial translation)
            var mongoFilter = TryBuildMongoFilter(resolved);

            return new CriteriaResult<BsonDocument>
            {
                Predicate = lambda.Compile(),
                MongoFilter = mongoFilter
            };
        }

        private string ReplaceTokens(string input)
        {
            foreach (var kvp in _tokenValues)
                input = input.Replace($"@{kvp.Key}", kvp.Value.ToString());

            return input;
        }

        private FilterDefinition<BsonDocument>? TryBuildMongoFilter(string criteria)
        {
            try
            {
                // Only handle simple equality and range operations
                // Example: "UserId == '123'" or "Age >= 18"
                var parts = criteria.Split(new[] { "&&", "||" }, StringSplitOptions.None);
                var filters = new List<FilterDefinition<BsonDocument>>();

                foreach (var part in parts)
                {
                    if (part.Contains("=="))
                    {
                        var kv = part.Split("==", StringSplitOptions.TrimEntries);
                        filters.Add(Builders<BsonDocument>.Filter.Eq(kv[0], kv[1].Trim('\'')));
                    }
                    else if (part.Contains(">="))
                    {
                        var kv = part.Split(">=", StringSplitOptions.TrimEntries);
                        filters.Add(Builders<BsonDocument>.Filter.Gte(kv[0], BsonValue.Create(kv[1])));
                    }
                    else if (part.Contains("<="))
                    {
                        var kv = part.Split("<=", StringSplitOptions.TrimEntries);
                        filters.Add(Builders<BsonDocument>.Filter.Lte(kv[0], BsonValue.Create(kv[1])));
                    }
                }

                return filters.Count == 0 ? Builders<BsonDocument>.Filter.Empty :
                       Builders<BsonDocument>.Filter.And(filters);
            }
            catch
            {
                return Builders<BsonDocument>.Filter.Empty;
            }
        }
    }


}
