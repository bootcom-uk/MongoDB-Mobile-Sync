using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Models
{
    public partial class APISyncProcessingDetail : ObservableObject
    {

        [ObservableProperty]
        string databaseName;

        [ObservableProperty]
        string collectionName;

        [ObservableProperty]
        int pageNumber;

        [ObservableProperty]
        int recordsProcessed;

    }
}
