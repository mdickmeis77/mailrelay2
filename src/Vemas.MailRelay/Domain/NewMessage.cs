namespace Vemas.MailRelay.Domain;

/// <summary>Neu eingegangene Mail, die in die Queue geschrieben wird.</summary>
public sealed class NewMessage
{
    public int? SmtpUserId { get; set; }
    public int ProfileId { get; set; }
    public string ClientIp { get; set; }
    public string EnvelopeFrom { get; set; }
    public IReadOnlyList<string> EnvelopeTo { get; set; } = [];
    public string HeaderFrom { get; set; }
    public string HeaderTo { get; set; }
    public string Subject { get; set; }
    public byte[] Mime { get; set; } = [];
}
