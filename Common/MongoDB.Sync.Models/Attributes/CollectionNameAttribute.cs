namespace MongoDB.Sync.Models.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CollectionNameAttribute : Attribute
    {

        public readonly string CollectionName;

        public CollectionNameAttribute(string collectionName)
        {
            CollectionName = collectionName;
        }

    }
}
