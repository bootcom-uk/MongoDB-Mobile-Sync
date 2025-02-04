using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoDB.Sync.Models
{
    public partial class AppSyncMapping : ObservableObject
    {

        [ObservableProperty]
        ObjectId id;

        [ObservableProperty]
        int version;

        [ObservableProperty]
        int fullRefreshIfNoActivityInDays;

        [ObservableProperty]
        List<CollectionMapping> collections;

        [ObservableProperty]
        string appName;

        [ObservableProperty]
        bool initialSyncComplete;
    }
}
