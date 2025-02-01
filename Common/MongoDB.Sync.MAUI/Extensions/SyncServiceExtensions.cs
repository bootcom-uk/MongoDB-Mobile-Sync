using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.MAUI.Models;
using MongoDB.Sync.Services;
using Microsoft.Extensions.Http.Resilience;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using Services;

namespace MongoDB.Sync.MAUI.Extensions
{
    public static class SyncServiceExtensions
    {

        public static MauiAppBuilder SetupSyncService(
        this MauiAppBuilder builder,
        Action<SyncOptions> syncOptionsAction)
        {

            // Configure SyncOptions and add HttpClientFactory
            builder.Services.AddHttpClient();
            builder.Services.Configure(syncOptionsAction);
            
            // Register SyncOptions as a singleton service
            builder.Services.AddSingleton(provider =>
            {
                var options = new SyncOptions();
                syncOptionsAction(options);
                return options;
            });

            // Register the local database service
            builder.Services.AddSingleton<LocalDatabaseService>(provider =>
            {
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new LocalDatabaseService(messenger, options.LiteDbPath);
            });


            builder.Services.AddSingleton<HttpService>();
            
            builder.Services.AddSingleton<ISyncService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new SyncService(provider.GetRequiredService<LocalDatabaseService>(), provider.GetRequiredService<HttpService>(), messenger, options.ApiUrl, options.AppName);
            });

            return builder;
        }

    }
}
