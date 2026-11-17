namespace Vemas.MailRelay.Domain;

/// <summary>Kunde (Mandant) für den Zugriff aus NextGen.</summary>
public sealed class Customer
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>SQL-Server-Logins, über die der Kunde erkannt wird (Login, mit dem NextGen auf die Datenbank zugreift).</summary>
    public List<string> Logins { get; set; } = [];
}
