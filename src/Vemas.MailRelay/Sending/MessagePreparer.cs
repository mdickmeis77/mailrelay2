using MimeKit;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Sending;

/// <summary>Passt die Mail vor dem Versand an: Absender je Profil-Modus und Bcc aus dem SMTP-Envelope.</summary>
public static class MessagePreparer
{
    /// <summary>Bereitet die Mail vor und liefert das Postfach, über das gesendet wird.</summary>
    public static string Prepare(MimeMessage message, RelayProfile profile, IReadOnlyList<string> envelopeRecipients)
    {
        string sender = profile.SenderMode == SenderMode.FixedSender
            ? ApplyFixedSender(message, profile)
            : ApplySendAs(message, profile);

        // Sender-Header ("im Auftrag von") würde Graph als abweichenden Absender werten.
        message.Sender = null;

        AddMissingEnvelopeRecipientsAsBcc(message, envelopeRecipients);

        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
        {
            throw new PermanentSendException("Mail hat keine Empfänger.");
        }

        return sender;
    }

    private static string ApplyFixedSender(MimeMessage message, RelayProfile profile)
    {
        string sender = profile.DefaultSender;
        var original = message.From.Mailboxes.FirstOrDefault();

        if (original is not null
            && !string.Equals(original.Address, sender, StringComparison.OrdinalIgnoreCase)
            && message.ReplyTo.Count == 0)
        {
            message.ReplyTo.Add(new MailboxAddress(original.Name, original.Address));
        }

        message.From.Clear();
        message.From.Add(new MailboxAddress(original?.Name ?? string.Empty, sender));
        return sender;
    }

    /// <summary>
    /// "Senden als": Versand immer über das Standard-Postfach des Profils (Konto A), der Absender aus der
    /// Mail (Konto B) bleibt im Kopf stehen. Exchange prüft, ob A das Recht "Senden als" auf B hat.
    /// </summary>
    private static string ApplySendAs(MimeMessage message, RelayProfile profile)
    {
        var from = message.From.Mailboxes.FirstOrDefault();
        if (from is null)
        {
            message.From.Add(MailboxAddress.Parse(profile.DefaultSender));
        }
        else if (!SenderPolicy.IsAllowed(profile, from.Address))
        {
            throw new PermanentSendException($"Absender '{from.Address}' ist für Profil '{profile.Name}' nicht erlaubt.");
        }

        return profile.DefaultSender;
    }

    /// <summary>
    /// Database Mail übergibt Bcc-Empfänger nur per RCPT TO. Graph liest Empfänger aus dem Header,
    /// daher werden fehlende Envelope-Empfänger als Bcc ergänzt.
    /// </summary>
    private static void AddMissingEnvelopeRecipientsAsBcc(MimeMessage message, IReadOnlyList<string> envelopeRecipients)
    {
        var known = new HashSet<string>(
            message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Select(m => m.Address),
            StringComparer.OrdinalIgnoreCase);

        foreach (string recipient in envelopeRecipients)
        {
            if (known.Add(recipient))
            {
                message.Bcc.Add(new MailboxAddress(string.Empty, recipient));
            }
        }
    }
}
