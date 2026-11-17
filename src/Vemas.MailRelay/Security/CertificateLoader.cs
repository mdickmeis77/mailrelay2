using System.Security.Cryptography.X509Certificates;

namespace Vemas.MailRelay.Security;

/// <summary>Lädt Zertifikate per Thumbprint aus dem Speicher LocalMachine\My.</summary>
public static class CertificateLoader
{
    /// <returns>Das Zertifikat oder null, wenn kein Thumbprint konfiguriert ist.</returns>
    /// <exception cref="InvalidOperationException">Thumbprint konfiguriert, aber Zertifikat nicht gefunden.</exception>
    public static X509Certificate2 FindInMachineStore(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }

        string normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Zertifikat mit Thumbprint {normalized} nicht in LocalMachine\\My gefunden.");
        }

        return matches[0];
    }
}
