using MimeKit;
using MimeKit.Text;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Sending;

/// <summary>Liest Kopfdaten, Text und Anhangsnamen für die Anzeige in NextGen aus einer Mail.</summary>
public static class MessageContentExtractor
{
    /// <summary>Maximale Länge des gespeicherten Mailtexts (ca. 100 KB).</summary>
    public const int MaxBodyLength = 100_000;

    private const string TruncatedSuffix = "\n\n[… Text gekürzt]";

    public static MessageContent Extract(MimeMessage message)
    {
        return new MessageContent
        {
            HeaderCc = JoinOrNull(message.Cc.Mailboxes.Select(m => m.Address)),
            ReplyTo = message.ReplyTo.Mailboxes.FirstOrDefault()?.Address,
            BodyText = Truncate(GetPlainText(message)),
            AttachmentNames = JoinOrNull(message.Attachments.Select(GetAttachmentName))
        };
    }

    private static string GetPlainText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody;
        }

        return string.IsNullOrWhiteSpace(message.HtmlBody) ? null : new HtmlToText().Convert(message.HtmlBody);
    }

    private static string GetAttachmentName(MimeEntity entity)
    {
        return entity switch
        {
            MimePart part when !string.IsNullOrEmpty(part.FileName) => part.FileName,
            MessagePart messagePart => (messagePart.Message?.Subject ?? "Mail") + ".eml",
            _ => entity.ContentType?.Name ?? entity.ContentType?.MimeType
        };
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxBodyLength)
        {
            return text;
        }

        return text[..(MaxBodyLength - TruncatedSuffix.Length)] + TruncatedSuffix;
    }

    private static string JoinOrNull(IEnumerable<string> values)
    {
        string joined = string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
        return joined.Length == 0 ? null : joined;
    }
}
