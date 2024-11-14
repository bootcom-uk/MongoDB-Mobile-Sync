using Microsoft.Extensions.Logging;
using MongoDB.Sync.MAUI.Extensions;
using MongoDBSyncDemo.Services;
using MongoDBSyncDemo.ViewModels.Authentication;
using MongoDBSyncDemo.Extensions;
using MongoDBSyncDemo.Views.Authentication;
using CommunityToolkit.Mvvm.Messaging;

namespace MongoDBSyncDemo
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

            builder.Services.SetupSyncService(options =>
            {
                options.AppName = "BOOTCOM_HOME";
                options.ApiUrl = "https://localhost:7045";                
            }, httpOptions =>
            {
                httpOptions.DeviceId = InternalSettings.DeviceId;
                httpOptions.RefreshToken = InternalSettings.RefreshToken;
                httpOptions.UserToken = InternalSettings.UserToken;                
            });

            builder.Services.AddSingleton<AuthenticationService>();
            builder.Services.AddSingleton<NavigationService>();
            builder.Services.AddSingleton<UserAuthenticationService>();
            
            builder.Services.AddView<AuthenticatePage, AuthenticatePageViewModel>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
