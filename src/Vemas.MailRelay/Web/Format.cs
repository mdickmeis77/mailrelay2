using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Web;

/// <summary>Anzeige-Helfer für die Razor Pages.</summary>
public static class Format
{
    private const int BytesPerKilobyte = 1024;

    public static string LocalTime(DateTime? utc)
    {
        return utc is null ? "–" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    }

    public static string Size(int bytes)
    {
        if (bytes < BytesPerKilobyte)
        {
            return $"{bytes} B";
        }

        double kb = bytes / (double)BytesPerKilobyte;
        return kb < BytesPerKilobyte ? $"{kb:0.#} KB" : $"{kb / BytesPerKilobyte:0.#} MB";
    }

    public static string StatusCss(string status)
    {
        return status switch
        {
            MessageStatus.Sent => "badge ok",
            MessageStatus.Failed => "badge err",
            MessageStatus.Retry => "badge warn",
            MessageStatus.Cancelled => "badge muted",
            _ => "badge info"
        };
    }

    public static string StatusText(string status)
    {
        return status switch
        {
            MessageStatus.Queued => "Wartend",
            MessageStatus.Sending => "Wird gesendet",
            MessageStatus.Retry => "Wiederholung",
            MessageStatus.Sent => "Gesendet",
            MessageStatus.Failed => "Fehlgeschlagen",
            MessageStatus.Cancelled => "Abgebrochen",
            _ => status
        };
    }

    public static string AttemptType(string attemptType)
    {
        return attemptType switch
        {
            "Send" => "Versand",
            "Resend" => "Neu senden",
            "Cancel" => "Abbrechen",
            "DbMail" => "Database Mail",
            _ => attemptType
        };
    }

    public static string Shorten(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)] + "…";
    }
}
