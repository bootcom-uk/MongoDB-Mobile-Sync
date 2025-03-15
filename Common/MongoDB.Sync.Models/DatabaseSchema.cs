namespace MongoDB.Sync.Models
{
    public class DatabaseSchema
    {
        public string DatabaseName { get; set; }
        public List<CollectionSchema> Collections { get; set; }
    }
}
