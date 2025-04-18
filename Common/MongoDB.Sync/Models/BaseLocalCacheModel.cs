using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using MongoDB.Sync.Converters;
using MongoDB.Sync.Models.Attributes;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Models
{
    public abstract partial class BaseLocalCacheModel : ObservableObject
    {
        [ObservableProperty]
        ObjectId id;

    }
}
