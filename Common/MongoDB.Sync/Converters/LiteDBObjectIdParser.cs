using LiteDB;

namespace MongoDB.Sync.Converters
{
    public static class LiteDBObjectIdParser
    {

            public static bool TryParse(string value, out ObjectId objectId)
            {
                try
                {
                    objectId = new ObjectId(value);
                    return true;
                }
                catch
                {
                    objectId = ObjectId.Empty;
                    return false;
                }
            }        

    }
}
