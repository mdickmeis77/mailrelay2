namespace Vemas.MailRelay.Options;

/// <summary>Einstellungen für Hintergrundaufgaben (Abschnitt "Maintenance").</summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Nach dieser Anzahl Tage wird der MIME-Inhalt (inkl. Anhänge) gelöscht.</summary>
    public int MimeRetentionDays { get; set; } = 30;

    /// <summary>Nach dieser Anzahl Tage wird die Protokollzeile gelöscht.</summary>
    public int LogRetentionDays { get; set; } = 365;

    /// <summary>So viele Tage vor Ablauf eines Client Secrets wird gewarnt.</summary>
    public int SecretExpiryWarningDays { get; set; } = 30;

    /// <summary>Intervall in Sekunden, in dem Profile/Benutzer neu geladen werden.</summary>
    public int ConfigRefreshSeconds { get; set; } = 60;
}
