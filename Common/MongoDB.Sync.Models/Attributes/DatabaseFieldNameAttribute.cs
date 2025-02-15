namespace MongoDB.Sync.Models.Attributes
{
    public sealed class DatabaseFieldNameAttribute : Attribute
    {

        public readonly string DatabaseFieldName;

        public DatabaseFieldNameAttribute(string databaseFieldName)
        {
            DatabaseFieldName = databaseFieldName;
        }

    }
}
