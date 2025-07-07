using MongoDB.Sync.Core.Services.Models.Models;
using MongoDB.Sync.LocalDataCache;

namespace MongoDB.Sync.MAUI.Extensions
{
    public static class LiveQueryableLiteCollectionExtensions
    {

        public static void AddRange<T>(this LiveQueryableLiteCollection<T> collection, IEnumerable<T> items) where T : BaseLocalCacheModel
        {
            foreach (var item in items)
                collection.Add(item);
        }

    }
}
