using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using MongoDB.Sync.Core.Services.Models.Services;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.MAUI.Models;
using MongoDB.Sync.Services;
using Services;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

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
            
            builder.Services.AddSingleton<NetworkStateService>();

            builder.Services.AddSingleton<BaseTypeResolverService>();

            // Register SyncOptions as a singleton service
            builder.Services.AddSingleton(provider =>
            {
                var options = new SyncOptions(provider.GetRequiredService<HttpService>());                
                syncOptionsAction(options);
                return options;
            });

            // Register the local database service
            builder.Services.AddSingleton<LocalDatabaseSyncService>(provider =>
            {
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new LocalDatabaseSyncService(messenger, options.LiteDbPath);
            });

            builder.Services.AddSingleton<HttpService>();
            
            builder.Services.AddSingleton<ISyncService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                var networkStateService = provider.GetRequiredService<NetworkStateService>();
                return new SyncService(provider.GetRequiredService<LocalDatabaseSyncService>(), provider.GetRequiredService<HttpService>(), messenger, networkStateService, options.ApiUrl, options.AppName, options.PreRequestAction, options.StatusChangeAction);
            });

            builder.Services.AddSingleton<LocalCacheService>(provider =>
            {
                var localDatabaseSyncService = provider.GetRequiredService<LocalDatabaseSyncService>();
                var logger = provider.GetRequiredService<ILogger<LocalCacheService>>();
                var options = provider.GetRequiredService<SyncOptions>();
                var httpService = provider.GetRequiredService<HttpService>();
                var messenger = provider.GetRequiredService<IMessenger>();

                return new LocalCacheService(messenger, localDatabaseSyncService, logger, httpService, options.ApiUrl, options.AppName, options.PreRequestAction, options.StatusChangeAction);
            });

            return builder;
        }

    }
}
