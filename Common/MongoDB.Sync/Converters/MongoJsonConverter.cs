using LiteDB;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MongoDB.Sync.Converters
{
    public static class MongoJsonConverter
    {

        public static string ConvertMongoJsonToLiteDB(string ejson)
        {
            var json = ejson;

            // Replace $oid
            json = Regex.Replace(json, @"\{\s*""\$oid""\s*:\s*""(?<oid>[a-fA-F0-9]+)""\s*\}", "\"$1\"");

            // Replace $numberLong (used inside $date)
            json = Regex.Replace(json, @"\{\s*""\$numberLong""\s*:\s*""(?<long>\d+)""\s*\}", "${long}");

            // Replace $date wrapper
            json = Regex.Replace(json, @"\{\s*""\$date""\s*:\s*(\d+)\s*\}", match =>
            {
                var millis = long.Parse(match.Groups[1].Value);
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
                return $"\"{dt:O}\""; // ISO 8601
            });

            return json;
        }

        private static BsonDocument ConvertJsonObject(JsonObject obj)
        {
            var bson = new BsonDocument();
            foreach (var property in obj)
            {
                bson[property.Key] = ConvertJsonValue(property.Key, property.Value);
            }
            return bson;
        }

        private static BsonValue ConvertJsonValue(string? key, JsonNode? node)
        {
            if (node == null) return BsonValue.Null;

            if (node is JsonValue value)
            {
                if (value.TryGetValue(out int i)) return i;
                if (value.TryGetValue(out long l)) return l;
                if (value.TryGetValue(out double d)) return d;
                if (value.TryGetValue(out bool b)) return b;
                if (value.TryGetValue(out string? s)) return s ?? BsonValue.Null;
            }

            if (node is JsonObject obj)
            {
                // Convert MongoDB '$numberDouble'
                if(obj.ContainsKey("$numberDouble") && obj["$numberDouble"] is JsonValue doubleValue)
                {
                    return Convert.ToDouble(doubleValue);
                }

                // Convert MongoDB `$date` → `DateTime`
                if (obj.ContainsKey("$date") && obj["$date"] is JsonObject dateObj && dateObj.ContainsKey("$numberLong"))
                {
                    long timestamp = long.Parse(dateObj["$numberLong"]!.ToString());
                    return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
                }

                // Convert MongoDB `_id` with `$oid` → LiteDB ObjectId
                if (key == "_id" && obj.ContainsKey("$oid"))
                {
                    return new ObjectId(obj["$oid"]!.ToString());
                }

                return ConvertJsonObject(obj);
            }

            if (node is JsonArray arr)
            {
                var bsonArray = new BsonArray();
                foreach (var item in arr)
                {
                    bsonArray.Add(ConvertJsonValue(null, item));
                }
                return bsonArray;
            }

            return BsonValue.Null;
        }

    }
}
