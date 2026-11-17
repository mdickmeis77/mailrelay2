namespace Vemas.MailRelay.Domain;

/// <summary>Testmail-Auftrag aus NextGen (mailrelay.TestMailRequest).</summary>
public sealed class TestMailRequest
{
    public int TestMailRequestId { get; set; }
    public int ProfileId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string RequestedBy { get; set; }
}
