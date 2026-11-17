namespace Vemas.MailRelay.Domain;

/// <summary>Eintrag aus mailrelay.Attempt.</summary>
public sealed class AttemptItem
{
    public DateTime AttemptAt { get; set; }
    public string AttemptType { get; set; } = string.Empty;
    public bool? Success { get; set; }
    public int? HttpStatus { get; set; }
    public string ErrorText { get; set; }
    public string GraphRequestId { get; set; }
    public string UserName { get; set; }
}
