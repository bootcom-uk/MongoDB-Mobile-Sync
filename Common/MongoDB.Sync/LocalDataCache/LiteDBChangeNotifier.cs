using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace MongoDB.Sync.LocalDataCache
{
    public class LiteDBChangeNotifier
    {
        private static readonly Dictionary<string, Subject<Unit>> _changeSubjects = new();

        public static IObservable<Unit> GetChangeStream(string collectionName)
        {
            if (!_changeSubjects.ContainsKey(collectionName))
                _changeSubjects[collectionName] = new Subject<Unit>();

            return _changeSubjects[collectionName];
        }

        public static void NotifyChange(string collectionName)
        {
            if (_changeSubjects.ContainsKey(collectionName))
                _changeSubjects[collectionName].OnNext(Unit.Default);
        }
    }
}
