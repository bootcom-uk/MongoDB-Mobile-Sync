using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.MAUI.Models;
using MongoDB.Sync.Services;
using System.Net.Http;

namespace MongoDB.Sync.MAUI.Extensions
{
    public static class SyncServiceExtensions
    {

        public static MauiAppBuilder SetupSyncService(
        this MauiAppBuilder builder,
        Action<SyncOptions> syncOptionsAction, Action<HttpSyncOptions> httpSyncOptionsAction)
        {
            // Configure SyncOptions and add HttpClientFactory
            builder.Services.Configure(syncOptionsAction);
            builder.Services.Configure(httpSyncOptionsAction);
            builder.Services.AddHttpClient();


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

            // Now include the instance of the http sync service (and options)
            builder.Services.AddSingleton(provider =>
            {
                var options = new HttpSyncOptions();
                httpSyncOptionsAction(options);
                return options;
            });

            builder.Services.AddSingleton<SyncHttpService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<HttpSyncOptions>();
                var syncHttpService = new SyncHttpService(httpClientFactory.CreateClient(Guid.NewGuid().ToString()), provider.GetRequiredService<IMessenger>());
                syncHttpService.DeviceId = options.DeviceId;
                syncHttpService.JwtToken = options.UserToken;
                syncHttpService.RefreshToken = options.RefreshToken;
                return syncHttpService;
            });
            
            builder.Services.AddSingleton<ISyncService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new SyncService(provider.GetRequiredService<LocalDatabaseService>(), provider.GetRequiredService<SyncHttpService>(), messenger, options.ApiUrl, options.AppName);
            });

            return builder;
        }

    }
}
