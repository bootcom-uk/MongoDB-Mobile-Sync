using LiteDB;
using System.Collections.Concurrent;

namespace MongoDB.Sync.LiteDb
{
    public class LiteDbQueueProcessor
    {
        private readonly LiteDatabase _liteDb;
        private readonly BlockingCollection<Func<Task>> _queue = new();
        private readonly CancellationTokenSource _cts = new();

        public LiteDbQueueProcessor(LiteDatabase liteDb)
        {
            _liteDb = liteDb;
            Task.Run(ProcessQueue);
        }

        public void Enqueue(Func<LiteDatabase, Task> work)
        {
            _queue.Add(() => work(_liteDb));
        }

        private async Task ProcessQueue()
        {
            foreach (var workItem in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    await workItem();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in LiteDbQueueProcessor: " + ex);
                    // Log to Sentry or elsewhere
                }
            }
        }

        public void Stop() => _cts.Cancel();
    }

}
