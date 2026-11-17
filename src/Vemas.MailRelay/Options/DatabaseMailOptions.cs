namespace Vemas.MailRelay.Options;

/// <summary>Rückmeldung an SQL Server Database Mail (Abschnitt "DatabaseMail").</summary>
public sealed class DatabaseMailOptions
{
    public const string SectionName = "DatabaseMail";

    /// <summary>
    /// Endgültig fehlgeschlagene Mails an Database Mail zurückmelden (msdb.dbo.mailrelay_StatusMelden,
    /// eingerichtet mit Install-DatabaseMail-Rueckmeldung.sql).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Verbindung zur SQL-Server-Instanz mit Database Mail. Leer: dieselbe Instanz wie die Relay-Datenbank (Datenbank msdb).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
