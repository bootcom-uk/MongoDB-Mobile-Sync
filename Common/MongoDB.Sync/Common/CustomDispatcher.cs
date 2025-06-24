
using System.Threading.Tasks;

namespace MongoDB.Sync.Common
{
    public static class CustomDispatcher
    {

        public static async Task Dispatch(Action action, CancellationToken token = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (token.IsCancellationRequested) return;

            if(Dispatcher.GetForCurrentThread() == null)
            {
                action.Invoke();
            }

            await Task.Run(action.Invoke, token);
        }

    }
}
