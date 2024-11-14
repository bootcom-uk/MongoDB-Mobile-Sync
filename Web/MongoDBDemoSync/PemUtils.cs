using System.Security.Cryptography;

namespace MongoDBDemoSync
{
    public static class PemUtils
    {
        // Converts a PEM-encoded private key string into an RSA object
        public static RSA ImportPrivateKey(string pem)
        {
            // Creates a new RSA instance
            var rsa = RSA.Create();

            // Imports the PEM string (formatted private key) into the RSA object
            rsa.ImportFromPem(pem.ToCharArray());

            return rsa; // Returns the RSA object containing the private key
        }

        // Converts a PEM-encoded public key string into an RSA object
        public static RSA ImportPublicKey(string pem)
        {
            // Creates a new RSA instance
            var rsa = RSA.Create();

            // Imports the PEM string (formatted public key) into the RSA object
            rsa.ImportFromPem(pem.ToCharArray());

            return rsa; // Returns the RSA object containing the public key
        }
    }
}
