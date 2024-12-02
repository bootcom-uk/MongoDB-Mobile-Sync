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

        public static IServiceCollection SetupSyncService(
        this IServiceCollection services,
        Action<SyncOptions> syncOptionsAction, Action<HttpSyncOptions> httpSyncOptionsAction)
        {
            // Configure SyncOptions and add HttpClientFactory
            services.Configure(syncOptionsAction);
            services.Configure(httpSyncOptionsAction);
            services.AddHttpClient();


            // Register SyncOptions as a singleton service
            services.AddSingleton(provider =>
            {
                var options = new SyncOptions();
                syncOptionsAction(options);
                return options;
            });

            // Register the local database service
            services.AddSingleton<LocalDatabaseService>(provider =>
            {
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new LocalDatabaseService(messenger, options.LiteDbPath);
            });

            // Now include the instance of the http sync service (and options)
            services.AddSingleton(provider =>
            {
                var options = new HttpSyncOptions();
                httpSyncOptionsAction(options);
                return options;
            });

            services.AddSingleton<SyncHttpService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<HttpSyncOptions>();
                var syncHttpService = new SyncHttpService(httpClientFactory.CreateClient(Guid.NewGuid().ToString()), provider.GetRequiredService<IMessenger>());
                syncHttpService.DeviceId = options.DeviceId;
                syncHttpService.JwtToken = options.UserToken;
                syncHttpService.RefreshToken = options.RefreshToken;
                return syncHttpService;
            });
            
            services.AddSingleton<ISyncService>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var options = provider.GetRequiredService<SyncOptions>();
                var messenger = provider.GetRequiredService<IMessenger>();
                return new SyncService(provider.GetRequiredService<LocalDatabaseService>(), provider.GetRequiredService<SyncHttpService>(), messenger, options.ApiUrl, options.AppName);
            });

            return services;
        }

    }
}
