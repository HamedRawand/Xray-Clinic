using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace NasimXray.Api.Services;

// Loads the server's HTTPS certificate (.pfx). Importing a .pfx also has to store its private key somewhere, and
// which place is allowed depends on the account the service runs as and on the PC's security settings: on some
// Windows installations "Local Service" is refused access to its own key store ("Access denied") even though the
// same file loads fine elsewhere. So try the key stores in order of preference instead of failing on the first.
public static class ServerCertificate
{
    public static X509Certificate2 Load(string path, string password)
    {
        var attempts = new (X509KeyStorageFlags Flags, string Label)[]
        {
            (X509KeyStorageFlags.DefaultKeySet, "the account's own key store"),
            (X509KeyStorageFlags.MachineKeySet, "the computer-wide key store"),
            (X509KeyStorageFlags.EphemeralKeySet, "memory only"),
        };

        var failures = new List<string>();
        for (var i = 0; i < attempts.Length; i++)
        {
            var (flags, label) = attempts[i];
            try
            {
                var certificate = new X509Certificate2(path, password, flags);
                if (i > 0)
                    Log.Warning("HTTPS certificate loaded using {KeyStore} (earlier attempts failed: {Failures})", label, string.Join("; ", failures));
                return certificate;
            }
            catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException)
            {
                failures.Add($"{label}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"The HTTPS certificate '{path}' could not be loaded ({string.Join("; ", failures)}).");
    }
}
