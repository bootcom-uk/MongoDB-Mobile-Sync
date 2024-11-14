using MongoDBSyncDemo.Services;

namespace MongoDBSyncDemo
{
    public partial class App : Application
    {
        public App(NavigationService navigationService)
        {
            InitializeComponent();

            MainPage = new NavigationPage();
            navigationService.NavigateToPage<Views.Authentication.AuthenticatePage>().ConfigureAwait(false);

        }
    }
}
