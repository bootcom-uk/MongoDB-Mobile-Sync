using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoDB.Sync.Messages
{
    /// <summary>
    /// When performing an initial sync when the app is first opened or when we resync
    /// </summary>
    public class APISyncMessageReceived : ValueChangedMessage<string>
    {
        public APISyncMessageReceived(string value) : base(value)
        {
        }
    }
}
