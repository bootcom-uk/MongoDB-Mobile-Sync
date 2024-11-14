using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MongoDBSyncDemo
{
    public static class InternalSettings
    {

        public static ObjectId? UserId
        {
            get
            {
                var serializedValue = Preferences.Default.Get(nameof(UserId), string.Empty);
                ObjectId? deserializedValue;
                if (string.IsNullOrEmpty(serializedValue))
                {
                    return null;
                }
                deserializedValue = JsonSerializer.Deserialize<ObjectId?>(serializedValue);
                return deserializedValue;
            }
            set
            {
                var serializedValue = JsonSerializer.Serialize(value, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });
                Preferences.Default.Set(nameof(UserId), serializedValue);
            }
        }

        public static string RefreshToken
        {
            get
            {
                var serializedValue = Preferences.Default.Get(nameof(RefreshToken), string.Empty);
                if (string.IsNullOrEmpty(serializedValue)) return string.Empty;
                var deSerializedValue = JsonSerializer.Deserialize<string>(serializedValue);
                return deSerializedValue!;
            }
            set
            {
                var serializedValue = JsonSerializer.Serialize(value, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });
                Preferences.Default.Set(nameof(RefreshToken), serializedValue);
            }
        }

        public static string UserToken
        {
            get
            {
                var serializedValue = Preferences.Default.Get(nameof(UserToken), string.Empty);
                if (string.IsNullOrEmpty(serializedValue)) return string.Empty;
                var deSerializedValue = JsonSerializer.Deserialize<string>(serializedValue);
                return deSerializedValue!;
            }
            set
            {
                var serializedValue = JsonSerializer.Serialize(value, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });
                Preferences.Default.Set(nameof(UserToken), serializedValue);
            }
        }

        public static string DeviceId
        {
            get
            {
                var serializedValue = Preferences.Default.Get(nameof(DeviceId), string.Empty);
                if (string.IsNullOrEmpty(serializedValue))
                {
                    var newDeviceId = Guid.NewGuid().ToString();
                    serializedValue = JsonSerializer.Serialize(newDeviceId, new JsonSerializerOptions()
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    Preferences.Default.Set(nameof(DeviceId), serializedValue);
                }
                var deSerializedValue = JsonSerializer.Deserialize<string>(serializedValue);
                return deSerializedValue!;
            }
            set
            {
                var serializedValue = JsonSerializer.Serialize(value, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });
                Preferences.Default.Set(nameof(DeviceId), serializedValue);
            }
        }

    }
}
