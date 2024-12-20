﻿namespace MongoDBAppSync
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder                
                .UsePrism(prism =>
                {
                    prism.RegisterTypes(containerRegistry =>
                    {
                        
                    });

                    prism.CreateWindow(async navigationService =>
                    {
                       await navigationService.NavigateAsync("");
                    });
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            return builder.Build();
        }
    }
}
