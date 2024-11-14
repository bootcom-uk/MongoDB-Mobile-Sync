using MongoDBSyncDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoDBSyncDemo.Extensions
{
    public static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddView<View, ViewModel>(this IServiceCollection services)
            where View : Page
            where ViewModel : ViewModelBase
        {
            services.AddTransient<View>();
            services.AddTransient<ViewModel>();

            return services;
        }

    }
}
