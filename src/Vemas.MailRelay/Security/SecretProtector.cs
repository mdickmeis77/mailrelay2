using System.Security.Cryptography;
using System.Text;

namespace Vemas.MailRelay.Security;

/// <summary>
/// Verschlüsselt Client Secrets per Windows DPAPI (Maschinenschlüssel).
/// Verschlüsselte Werte lassen sich nur auf dem Server entschlüsseln, auf dem sie erzeugt wurden.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Vemas.MailRelay.ClientSecret.v1");

    public static byte[] Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.LocalMachine);
    }

    /// <exception cref="CryptographicException">Wert wurde auf einem anderen Server verschlüsselt.</exception>
    public static string Unprotect(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedSecret, Entropy, DataProtectionScope.LocalMachine));
    }
}
