using Microsoft.Extensions.Configuration;

namespace MongoDB.Sync.Web.Models.Configuration
{
    public class APIConfiguration
    {

        [ConfigurationKeyName("mongo")]
        public required MongoConfiguration MongoConfigurationSection { get; set; }

        [ConfigurationKeyName("sentry")]
        public required SentryConfiguration SentryConfigurationSection { get; set; }

        [ConfigurationKeyName("token")]
        public required TokenConfiguration TokenConfigurationSection { get; set; }

    }
}
