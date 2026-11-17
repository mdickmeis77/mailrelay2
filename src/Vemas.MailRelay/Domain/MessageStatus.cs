namespace Vemas.MailRelay.Domain;

/// <summary>Statuswerte einer Mail (entsprechen CK_mailrelay_Message_Status).</summary>
public static class MessageStatus
{
    public const string Queued = "Queued";
    public const string Sending = "Sending";
    public const string Retry = "Retry";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Queued, Sending, Retry, Sent, Failed, Cancelled];
}
