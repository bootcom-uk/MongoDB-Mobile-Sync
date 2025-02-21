using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using MongoDB.Sync.Models.Attributes;

namespace MongoDB.Sync.Models
{
    public abstract partial class BaseLocalCacheModel : ObservableObject
    {
        [ObservableProperty]
        ObjectId id;

    }
}
