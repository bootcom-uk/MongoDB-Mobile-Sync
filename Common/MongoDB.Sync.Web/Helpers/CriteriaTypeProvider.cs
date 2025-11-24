using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Linq.Dynamic.Core;

namespace MongoDB.Sync.Web.Helpers
{
    public class CriteriaTypeProvider : DefaultDynamicLinqCustomTypeProvider
    {
        public CriteriaTypeProvider()
            : base(ParsingConfig.Default, [], true)
        {
        }

        public override HashSet<Type> GetCustomTypes()
        {
            var types = base.GetCustomTypes();
            types.Add(typeof(DateTime));
            types.Add(typeof(TimeSpan));
            return types;
        }
    }
}
