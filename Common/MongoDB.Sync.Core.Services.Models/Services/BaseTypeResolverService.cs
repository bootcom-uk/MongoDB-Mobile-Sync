using MongoDB.Sync.Core.Services.Models.Models;
using MongoDB.Sync.Models.Attributes;
using System.Collections.Immutable;
using System.Reflection;

namespace MongoDB.Sync.Core.Services.Models.Services
{
    public class BaseTypeResolverService
    {

        public readonly ImmutableDictionary<Type, List<PropertyInfo>> MappedTypes;

        public BaseTypeResolverService()
        {
            var mappedTypes = new Dictionary<Type, List<PropertyInfo>>(); // Initialize the dictionary
            var assemblies = new List<Assembly>();
            assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)));

            var allTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t =>
                    t.IsClass &&
                    typeof(BaseLocalCacheModel).IsAssignableFrom(t) &&
                    t.GetCustomAttribute<CollectionNameAttribute>() != null);

            foreach (var refType in allTypes)
            {

                if (refType.GetCustomAttribute<CollectionNameAttribute>() is null) continue;

                var baseModels = refType.GetProperties()
                    .Where(t => typeof(BaseLocalCacheModel).IsAssignableFrom(t.PropertyType));

                mappedTypes.Add(refType, baseModels.ToList());

            }

            MappedTypes = mappedTypes.ToImmutableDictionary();
        }

        public Type? CollectionNameToType(string collectionName)
        {
            foreach (var item in MappedTypes)
            {
                var attribute = item.Key.GetCustomAttribute<CollectionNameAttribute>();
                if (attribute is null) continue;
                if (attribute.CollectionName == collectionName)
                {
                    return item.Key;
                }
            }
            return null;
        }

    }
}
