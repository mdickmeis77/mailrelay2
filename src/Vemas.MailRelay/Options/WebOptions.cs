namespace Vemas.MailRelay.Options;

/// <summary>Einstellungen der Weboberfläche (Abschnitt "Web").</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    public int Port { get; set; } = 8587;

    /// <summary>AD-Gruppe, deren Mitglieder die Oberfläche nutzen dürfen, z.B. "DOMAENE\MailRelay-Admins".</summary>
    public string AdminGroup { get; set; } = @"BUILTIN\Administrators";

    /// <summary>Optional: Thumbprint eines Zertifikats (LocalMachine\My) für HTTPS.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;
}
