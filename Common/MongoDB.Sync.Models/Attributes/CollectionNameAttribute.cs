namespace MongoDB.Sync.Models.Attributes
{
    public sealed class CollectionNameAttribute : Attribute
    {

        public readonly string CollectionName;

        public CollectionNameAttribute(string collectionName)
        {
            CollectionName = collectionName;
        }

    }
}
