using CommunityToolkit.Mvvm.ComponentModel;
using MongoDBSyncDemo.Services;

namespace MongoDBSyncDemo.ViewModels
{
    public abstract class ViewModelBase : ObservableObject
    {

        readonly NavigationService _navigationService;

        protected ViewModelBase(NavigationService navigationService) { 
            _navigationService = navigationService;
        }

        public virtual Task OnNavigatingTo(object? parameter)
    => Task.CompletedTask;

        public virtual Task OnNavigatedFrom(bool isForwardNavigation)
            => Task.CompletedTask;

        public virtual Task OnNavigatedTo()
            => Task.CompletedTask;

    }
}
