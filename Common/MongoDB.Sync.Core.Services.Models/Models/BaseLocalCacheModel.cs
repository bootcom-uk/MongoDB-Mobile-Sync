using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace MongoDB.Sync.Core.Services.Models.Models
{
    public abstract partial class BaseLocalCacheModel : ObservableObject
    {
        [ObservableProperty]
        ObjectId id;

    }
}
