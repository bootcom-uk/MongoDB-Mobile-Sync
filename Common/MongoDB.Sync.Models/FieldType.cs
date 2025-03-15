
namespace MongoDB.Sync.Models
{
    public class FieldType
    {
        public string Type { get; set; }
        public List<FieldSchema> NestedFields { get; set; } = new();
    }
}
