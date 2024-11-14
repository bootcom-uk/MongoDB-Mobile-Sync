using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoDB.Sync.Models
{
    public partial class CollectionMapping : ObservableObject
    {

        [ObservableProperty]
        string collectionName;

        [ObservableProperty]
        string databaseName;

        [ObservableProperty]
        List<string> fields;

        [ObservableProperty]
        int version;

        [ObservableProperty]
        ObjectId lastId;

    }
}
