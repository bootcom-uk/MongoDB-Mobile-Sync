namespace MongoDBDemoSync.Configuration
{
    public class TokenConfiguration
    {

        public required string Issuer { get; set; }

        public string? PublicKey { get; set; }

        [ConfigurationKeyName("AvailableAudience")]
        public string[] Audience { get; set; }

    }
}
