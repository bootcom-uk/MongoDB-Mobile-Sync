using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MongoDB.Sync.Messages
{
    public class AuthenticationTokensChangedMessage : ValueChangedMessage<Dictionary<string, string>>
    {
        public AuthenticationTokensChangedMessage(Dictionary<string, string> value) : base(value)
        {
        }
    }
}
