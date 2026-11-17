namespace Vemas.MailRelay.Options;

/// <summary>Einstellungen des SMTP-Eingangs (Abschnitt "Smtp").</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Lausch-Port, Standard 587.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Name in der SMTP-Begrüßung.</summary>
    public string ServerName { get; set; } = "Vemas.MailRelay";

    /// <summary>Maximale Mailgröße in MB.</summary>
    public int MaxMessageSizeMb { get; set; } = 150;

    /// <summary>Optional: Thumbprint eines Zertifikats (LocalMachine\My) für STARTTLS.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>Nur relevant mit Zertifikat: AUTH erst nach STARTTLS erlauben.</summary>
    public bool RequireTls { get; set; }
}
