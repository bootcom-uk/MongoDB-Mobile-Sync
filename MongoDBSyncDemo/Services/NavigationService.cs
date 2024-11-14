using MongoDBSyncDemo.ViewModels;
using System.Diagnostics;

namespace MongoDBSyncDemo.Services
{
    // Adapted from https://github.com/PieEatingNinjas/MAUI_MVVM_Demo/tree/main
    public class NavigationService
    {
        readonly IServiceProvider _services;

        protected INavigation Navigation
        {
            get
            {
                INavigation? navigation = Application.Current?.MainPage?.Navigation;
                if (navigation is not null)
                    return navigation;
                else
                {
                    //This is not good!
                    if (Debugger.IsAttached)
                        Debugger.Break();
                    throw new Exception();
                }
            }
        }

        public NavigationService(IServiceProvider services)
            => _services = services;


        public Task NavigateBack()
        {
            if (Navigation.NavigationStack.Count > 1)
                return Navigation.PopAsync();

            throw new InvalidOperationException("No pages to navigate back to!");
        }

        public async Task NavigateToPage<T>(object? parameter = null, bool removePreviousPage = true) where T : Page
        {
            if (Navigation.NavigationStack is not null && Navigation.NavigationStack.Count > 0 && removePreviousPage) await Navigation.PopToRootAsync();

            var toPage = ResolvePage<T>();

            if (toPage is not null)
            {
                //Subscribe to the toPage's NavigatedTo event
                toPage.NavigatedTo += Page_NavigatedTo;

                //Get VM of the toPage
                var toViewModel = GetPageViewModelBase(toPage);

                //Call navigatingTo on VM, passing in the paramter
                if (toViewModel is not null)
                    await toViewModel.OnNavigatingTo(parameter);

                //Navigate to requested page
                await Navigation.PushAsync(toPage, true);

                //Subscribe to the toPage's NavigatedFrom event
                toPage.NavigatedFrom += Page_NavigatedFrom;
            }
            else
                throw new InvalidOperationException($"Unable to resolve type {typeof(T).FullName}");
        }

        private async void Page_NavigatedFrom(object? sender, NavigatedFromEventArgs e)
        {
            //To determine forward navigation, we look at the 2nd to last item on the NavigationStack
            //If that entry equals the sender, it means we navigated forward from the sender to another page
            bool isForwardNavigation = Navigation.NavigationStack.Count > 1
                && Navigation.NavigationStack[^2] == sender;

            if (sender is Page thisPage)
            {
                if (!isForwardNavigation)
                {
                    thisPage.NavigatedTo -= Page_NavigatedTo;
                    thisPage.NavigatedFrom -= Page_NavigatedFrom;
                }

                await CallNavigatedFrom(thisPage, isForwardNavigation);
            }
        }

        private Task CallNavigatedFrom(Page p, bool isForward)
        {
            var fromViewModel = GetPageViewModelBase(p);

            if (fromViewModel is not null)
                return fromViewModel.OnNavigatedFrom(isForward);
            return Task.CompletedTask;
        }

        private async void Page_NavigatedTo(object? sender, NavigatedToEventArgs e)
            => await CallNavigatedTo(sender as Page);

        private Task CallNavigatedTo(Page? p)
        {
            var fromViewModel = GetPageViewModelBase(p);

            if (fromViewModel is not null)
                return fromViewModel.OnNavigatedTo();
            return Task.CompletedTask;
        }

        public Type? GetTypeByName(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Reverse())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private ViewModelBase? GetPageViewModelBase(Page? page)
        {
            if (page is null) return null;

            if (page.BindingContext is not null) return page.BindingContext as ViewModelBase;

            var suggestedViewModelName = $"{page.GetType().Name}ViewModel";
            var suggestedViewType = GetTypeByName(suggestedViewModelName);

            if (suggestedViewType is null) return null;

            page.BindingContext = _services.GetService(suggestedViewType);

            return page.BindingContext as ViewModelBase;
        }


        private T? ResolvePage<T>() where T : Page
            => _services.GetService<T>();
    }
}
