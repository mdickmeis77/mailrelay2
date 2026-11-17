namespace Vemas.MailRelay.Domain;

/// <summary>Zum Versand reservierte Mail aus der Queue.</summary>
public sealed class QueuedMessage
{
    public long MessageId { get; set; }
    public int ProfileId { get; set; }
    public string EnvelopeTo { get; set; } = string.Empty;
    public byte[] Mime { get; set; }
    public int AttemptCount { get; set; }
    public string Subject { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Zugeordnete Mail in msdb.dbo.sysmail_mailitems, sofern ein Fehler an Database Mail gemeldet wurde.</summary>
    public int? DbMailItemId { get; set; }

    public IReadOnlyList<string> GetEnvelopeRecipients()
    {
        return EnvelopeTo.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
