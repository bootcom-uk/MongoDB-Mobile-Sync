using MongoDB.Driver;

namespace MongoDB.Sync.Web.Models
{
    public class CriteriaResult<T>
    {
        public FilterDefinition<T>? MongoFilter { get; set; }
        public Func<T, bool>? Predicate { get; set; }
    }
}
