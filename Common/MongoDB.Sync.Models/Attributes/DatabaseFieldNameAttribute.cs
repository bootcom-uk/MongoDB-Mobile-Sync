namespace MongoDB.Sync.Models.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DatabaseFieldNameAttribute : Attribute
    {

        public readonly string DatabaseFieldName;

        public DatabaseFieldNameAttribute(string databaseFieldName)
        {
            DatabaseFieldName = databaseFieldName;
        }

    }
}
