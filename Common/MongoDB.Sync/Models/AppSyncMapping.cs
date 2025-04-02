using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using MongoDB.Sync.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MongoDB.Sync.Models
{
    public partial class AppSyncMapping : ObservableObject
    {

        [JsonConverter(typeof(ObjectIdConverter))]
        [ObservableProperty]
        ObjectId id = ObjectId.Empty;

        [ObservableProperty]
        int version;

        [ObservableProperty]
        int fullRefreshIfNoActivityInDays;

        [ObservableProperty]
        List<CollectionMapping> collections = new();

        [ObservableProperty]
        string appName = string.Empty;

        [ObservableProperty]
        bool initialSyncComplete;

        [ObservableProperty]
        DateTime? serverDateTime;
    }
}
