namespace Vemas.MailRelay.Domain;

/// <summary>Detailansicht einer Mail inkl. aller Versuche.</summary>
public sealed class MessageDetails
{
    public MessageListItem Summary { get; set; } = new();
    public string ClientIp { get; set; }
    public string EnvelopeFrom { get; set; }
    public string HeaderTo { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? MimePurgedAt { get; set; }
    public List<AttemptItem> Attempts { get; set; } = [];
}
