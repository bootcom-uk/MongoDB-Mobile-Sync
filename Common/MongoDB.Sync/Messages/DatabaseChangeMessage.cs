using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Messages
{
    public class DatabaseChangeMessage : ValueChangedMessage<DatabaseChangeParameters>
    {
        

        public DatabaseChangeMessage(DatabaseChangeParameters databaseChangeParameters) : base(databaseChangeParameters)
        {

        }
    }
}
