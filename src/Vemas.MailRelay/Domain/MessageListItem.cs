namespace Vemas.MailRelay.Domain;

/// <summary>Zeile in der Protokoll-Übersicht.</summary>
public sealed class MessageListItem
{
    public long MessageId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string SmtpUser { get; set; }
    public string ProfileName { get; set; }
    public string CustomerName { get; set; }
    public string HeaderFrom { get; set; }
    public string EnvelopeTo { get; set; }
    public string Subject { get; set; }
    public int SizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime? SentAt { get; set; }
    public string LastError { get; set; }
    public bool HasMime { get; set; }
}
