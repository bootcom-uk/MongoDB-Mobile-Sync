using LiteDB;
using MongoDB.Sync.Models.Attributes;
using System.Collections;
using System.Reflection;

namespace MongoDB.Sync.LiteDb
{
    public static class TypeProcessor
    {

        public static T? ProcessTypeForDeserialization<T>(T item, BsonDocument bson, LiteDB.LiteDatabase db)
        {

            if (bson is null) return default(T?);

            var itemType = item!.GetType();

            // Loop through the bson document
            foreach (var kvp in (BsonDocument)bson)
            {
                var key = kvp.Key;

                // If the field name is _id , change it to Id as this is our common convention  
                if (key == "_id") key = "Id";

                // Is this a property we're storing in the database but isn't part of the type continue
                if (itemType.GetProperty(key) is null) continue;

                // Process generic types
                var processedGenericType = ProcessGenericTypeForDeserialization(key, kvp.Value, itemType, db);
                if (processedGenericType.Item1)
                {
                    itemType.GetProperty(key)?.SetValue(item, processedGenericType.Item2);
                    continue;
                }

                // Process collection types
                var processedCollectionType = ProcessCollectionTypeForDeserialization(key, kvp.Value, itemType, db);
                if (processedCollectionType.Item1)
                {
                    itemType.GetProperty(key)?.SetValue(item, processedCollectionType.Item2);
                    continue;
                }

                var unmetCreds = key;

            }

            return item;
        }

        internal static (bool, Object?) ProcessCollectionTypeForDeserialization(string key, BsonValue value, Type itemType, LiteDB.LiteDatabase db)
        {
            if (!value.IsArray) return (false, null);

            var propertyInfo = itemType.GetProperty(key);

            if (propertyInfo?.PropertyType is null || propertyInfo?.PropertyType?.GenericTypeArguments is null || propertyInfo?.PropertyType?.GenericTypeArguments.Count() == 0) return (false, null);

            var listType = typeof(List<>).MakeGenericType(propertyInfo?.PropertyType?.GenericTypeArguments[0]!);
            var underlyingList = (IList?)Activator.CreateInstance(listType);

            if (underlyingList is null) return (false, null);

            // If this object type doesn't derive from BaseLocalCacheModel then
            // we need to set the value directly
            var listItemType = propertyInfo?.PropertyType?.GenericTypeArguments[0]!;

            if (listItemType.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName == null)
            {

                foreach (var itemValue in value.AsArray)
                {
                    var listItemObject = Activator.CreateInstance(listItemType);

                    foreach (var prpInfo in listItemType.GetProperties())
                    {

                        if (itemValue[prpInfo.Name].IsObjectId && prpInfo.PropertyType != typeof(ObjectId))
                        {
                            var collectionName = prpInfo.PropertyType.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;
                            if (string.IsNullOrWhiteSpace(collectionName)) continue;
                            var bsonObject = db.GetCollection(collectionName).FindById(itemValue[prpInfo.Name].AsObjectId);
                            if (bsonObject is null) continue;
                            var nestedObject = BsonMapper.Global.Deserialize(prpInfo.PropertyType, bsonObject);

                            // 🔥 RECURSION TIME
                            nestedObject = ProcessTypeForDeserialization(nestedObject, bsonObject, db);

                            prpInfo.SetValue(listItemObject, nestedObject);
                            continue;
                        }

                        var deserializedValue = BsonMapper.Global.Deserialize(prpInfo.PropertyType, itemValue[prpInfo.Name]);

                        // 🔥 If it's an object (not primitive) and has properties, recurse
                        if (deserializedValue != null && !IsPrimitiveOrString(prpInfo.PropertyType))
                        {
                            deserializedValue = ProcessTypeForDeserialization(deserializedValue, itemValue[prpInfo.Name].AsDocument, db);
                        }

                        prpInfo.SetValue(listItemObject, deserializedValue);
                    }

                    underlyingList.Add(listItemObject);
                }

                return (true, underlyingList);
            }

            // This object derives from BaseLocalCacheModel so we need to set the value
            foreach (var itemValue in value.AsArray)
            {
                var collectionName = propertyInfo!.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;
                var displayObject = db.GetCollection(collectionName).FindById(itemValue.ToString());
                underlyingList.Add(displayObject);
            }
            return (true, underlyingList);


        }

        internal static (bool, object?) ProcessGenericTypeForDeserialization(string key, BsonValue value, Type itemType, LiteDB.LiteDatabase db)
        {

            // Strangely (strange because i haven't looked into it) is that we receive a document with a single 
            // property in it. This is the case when we have a nested object and we want to display it as such
            if(value != null && value.IsDocument && value.AsDocument.Keys.Count == 1)
            {

                var id = value.AsDocument.First();

                if (id.Value.IsObjectId)
                {
                    var internalId = new ObjectId(id.Value.AsString);

                    var nestedObject = BsonMapper.Global.Deserialize(itemType.GetProperty(key)!.PropertyType, internalId);

                    // 🔥 RECURSION TIME
                    nestedObject = ProcessTypeForDeserialization(nestedObject, value.AsDocument, db);
                    return (true, nestedObject);
                }

                if (value.IsNull) return (true, null);
                if (value.IsBoolean) return (true, value.AsBoolean);
                if (value.IsString) return (true, value.AsString);
                if (value.IsInt32) return (true, value.AsInt32);
                if (value.IsInt64) return (true, value.AsInt64);
                if (value.IsDouble) return (true, value.AsDouble);
                if (value.IsDecimal) return (true, value.AsDecimal);
                if (value.IsDateTime) return (true, value.AsDateTime);
                if (value.IsGuid) return (true, value.AsGuid);
                if (value.IsBinary) return (true, value.AsBinary);

            }

            // If this property is being stored as a string but displayed as an object id handle that here
            if (itemType.GetProperty(key)!.PropertyType == typeof(ObjectId) && !value.IsNull && value.IsString) return (true, new ObjectId(value.AsString));

            // If we're processing an object id here its because we belong to a collection and want to 
            // display the full object even though we're only storing the id in the database
            if (value != null && value.IsObjectId && itemType.GetProperty(key)!.PropertyType != typeof(ObjectId))
            {
                var collectionName = itemType.GetProperty(key)!.PropertyType.GetCustomAttribute<CollectionNameAttribute>()?.CollectionName;
                if (string.IsNullOrWhiteSpace(collectionName)) return (false, null);

                var bsonObject = db.GetCollection(collectionName).FindById(value.AsObjectId);

                if (bsonObject is null) return (false, null);

                var nestedObject = BsonMapper.Global.Deserialize(itemType.GetProperty(key)!.PropertyType, bsonObject);

                // 🔥 RECURSION TIME
                nestedObject = ProcessTypeForDeserialization(nestedObject, bsonObject, db);

                return (true, nestedObject);
            }

            // Handle generic types
            if (value.IsNull) return (true, null);
            if (value.IsBoolean) return (true, value.AsBoolean);            
            if (value.IsString) return (true, value.AsString);
            if (value.IsInt32) return (true, value.AsInt32);
            if (value.IsInt64) return (true, value.AsInt64);
            if (value.IsDouble) return (true, value.AsDouble);
            if (value.IsDecimal) return (true, value.AsDecimal);
            if (value.IsDateTime) return (true, value.AsDateTime);
            if (value.IsGuid) return (true, value.AsGuid);
            if (value.IsBinary) return (true, value.AsBinary);
            if (value.IsObjectId && itemType.GetProperty(key)!.PropertyType == typeof(ObjectId)) return (true, value.AsObjectId);

            return (false, null);
        }

        private static bool IsPrimitiveOrString(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);
        }

        public static BsonDocument ProcessTypeForSerialization<T>(T item)
        {
            var bson = new BsonDocument();

            var itemType = item!.GetType();

            foreach (var prop in itemType.GetProperties())
            {
                var propName = prop.Name == "Id" ? "_id" : prop.Name;
                var value = prop.GetValue(item);

                if (value == null)
                {
                    bson[propName] = BsonValue.Null;
                    continue;
                }

                // Collections
                if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                {
                    var array = new BsonArray();
                    foreach (var element in (IEnumerable)value)
                    {
                        var elementType = element.GetType();

                        if (elementType.GetCustomAttribute<CollectionNameAttribute>() != null)
                        {
                            var idProp = elementType.GetProperty("Id");
                            if (idProp != null)
                            {
                                var idValue = idProp.GetValue(element);
                                array.Add(new BsonValue(idValue));
                            }
                        }
                        else
                        {
                            array.Add(ProcessTypeForSerialization(element));
                        }
                    }
                    bson[propName] = array;
                    continue;
                }

                // Single referenced object
                if (prop.PropertyType.GetCustomAttribute<CollectionNameAttribute>() != null)
                {
                    var idProp = prop.PropertyType.GetProperty("Id");
                    if (idProp != null)
                    {
                        var idValue = idProp.GetValue(value);
                        bson[propName] = new BsonValue(idValue);
                    }
                    continue;
                }

                // Primitive types
                if (IsPrimitiveOrString(prop.PropertyType) || (prop.PropertyType == typeof(ObjectId) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(ObjectId)))
                {
                    bson[propName] = new BsonValue(value);
                    continue;
                }

                // Embedded object (recursively serialize)
                bson[propName] = ProcessTypeForSerialization(value);
            }

            return bson;
        }


    }
}