
namespace MongoDB.Sync.Models
{
    public class CollectionSchema
    {
        public string CollectionName { get; set; }
        public List<FieldSchema> Fields { get; set; }
    }
}
