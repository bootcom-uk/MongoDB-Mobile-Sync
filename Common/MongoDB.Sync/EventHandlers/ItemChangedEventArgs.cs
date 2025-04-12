
namespace MongoDB.Sync.EventHandlers
{
    public class ItemChangedEventArgs<T> : EventArgs
    {
        public enum CollectionChangeType { Added, Removed, Updated }

        public CollectionChangeType ChangeType { get; }
        public T? ChangedItem { get; }
        public string? PropertyName { get; }

        public ItemChangedEventArgs(CollectionChangeType changeType, T? item, string? propertyName = null)
        {
            ChangeType = changeType;
            ChangedItem = item;
            PropertyName = propertyName;
        }
    }
}
