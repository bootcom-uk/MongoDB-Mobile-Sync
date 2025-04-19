using LiteDB;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace MongoDB.Sync.Converters
{
    public static class EjsonConverter
    {
        public static BsonDocument NormalizeEjson(JsonElement ejson)
        {
            var doc = new BsonDocument();
            foreach (var prop in ejson.EnumerateObject())
            {
                doc[prop.Name] = ConvertToBsonValue(prop.Value);
            }
            return doc;
        }

        private static BsonValue ConvertToBsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    // Handle Mongo Extended JSON types
                    if (element.TryGetProperty("$oid", out var oid))
                        return new BsonValue(new ObjectId(oid.GetString()));

                    if (element.TryGetProperty("$numberDouble", out var dbl))
                        return new BsonValue(double.Parse(dbl.GetString()));

                    if (element.TryGetProperty("$numberLong", out var lng))
                        return new BsonValue(long.Parse(lng.GetString()));

                    if (element.TryGetProperty("$date", out var dateElement))
                    {
                        if (dateElement.ValueKind == JsonValueKind.String)
                            return new BsonValue(DateTime.Parse(dateElement.GetString(), null, DateTimeStyles.AdjustToUniversal));

                        if (dateElement.TryGetProperty("$numberLong", out var ms))
                        {
                            var unixMillis = long.Parse(ms.GetString());
                            return new BsonValue(DateTimeOffset.FromUnixTimeMilliseconds(unixMillis).UtcDateTime);
                        }
                    }

                    // If it's a normal object, recurse
                    var subDoc = new BsonDocument();
                    foreach (var prop in element.EnumerateObject())
                    {
                        subDoc[prop.Name] = ConvertToBsonValue(prop.Value);
                    }
                    return subDoc;

                case JsonValueKind.Array:
                    var array = new BsonArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Add(ConvertToBsonValue(item));
                    }
                    return array;

                case JsonValueKind.String:
                    return new BsonValue(element.GetString());

                case JsonValueKind.Number:
                    return element.TryGetInt64(out var l) ? new BsonValue(l) : new BsonValue(element.GetDouble());

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new BsonValue(element.GetBoolean());

                case JsonValueKind.Null:
                default:
                    return BsonValue.Null;
            }
        }
    }



}
