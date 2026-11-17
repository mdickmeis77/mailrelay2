namespace Vemas.MailRelay.Domain;

/// <summary>Aus der Mail gelesene Anzeigedaten (Kopfdaten, Text, Anhangsnamen).</summary>
public sealed class MessageContent
{
    public string HeaderCc { get; set; }
    public string ReplyTo { get; set; }
    public string BodyText { get; set; }
    public string AttachmentNames { get; set; }
}
