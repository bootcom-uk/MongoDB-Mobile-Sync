using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Messages
{
    public class APISyncStartedMessage : ValueChangedMessage<bool>
    {
        public APISyncStartedMessage(bool value) : base(value)
        {
        }
    }
}
