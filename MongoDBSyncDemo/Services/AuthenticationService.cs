using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Messages;

namespace MongoDBSyncDemo.Services
{
    public class AuthenticationService
    {

        public AuthenticationService(IMessenger messenger) {
            messenger.Register<AuthenticationTokensChangedMessage>(this, (r, m) =>
            {
                InternalSettings.RefreshToken = m.Value["RefreshToken"];
                InternalSettings.UserToken = m.Value["JwtToken"];
            });
        }

    }
}
