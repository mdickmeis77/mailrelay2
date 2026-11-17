using SmtpServer.Mail;

namespace Vemas.MailRelay.Smtp;

internal static class MailboxExtensions
{
    /// <summary>"user@host"; leerer Absender (Bounce, "MAIL FROM:&lt;&gt;") ergibt einen Leerstring.</summary>
    public static string AsAddressString(this IMailbox mailbox)
    {
        if (mailbox is null || string.IsNullOrEmpty(mailbox.User))
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(mailbox.Host) ? mailbox.User : $"{mailbox.User}@{mailbox.Host}";
    }
}
