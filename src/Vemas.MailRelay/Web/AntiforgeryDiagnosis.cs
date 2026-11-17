using Microsoft.AspNetCore.Antiforgery;

namespace Vemas.MailRelay.Web;

/// <summary>Verständlicher Grund für einen abgelehnten Formular-Fälschungsschutz.</summary>
public sealed class AntiforgeryDiagnosis
{
    private const string FormFieldName = "__RequestVerificationToken";
    private const string CookiePrefix = ".AspNetCore.Antiforgery.";

    public string Reason { get; init; } = string.Empty;
    public string Advice { get; init; } = string.Empty;
    public string TechnicalMessage { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public bool HasCookie { get; init; }
    public bool HasFormField { get; init; }
    public string ReturnUrl { get; init; } = "/";

    public static AntiforgeryDiagnosis Create(HttpContext context, AntiforgeryValidationException exception)
    {
        var request = context.Request;
        bool hasCookie = request.Cookies.Keys.Any(k => k.StartsWith(CookiePrefix, StringComparison.Ordinal));
        bool hasField = request.HasFormContentType && request.Form.ContainsKey(FormFieldName);
        string message = exception.Message;

        (string reason, string advice) = (hasCookie, hasField) switch
        {
            (_, false) when (request.ContentLength ?? 0) == 0 =>
                ("Das Formular kam leer beim Dienst an.",
                 "Das passiert, wenn der Browser die Windows-Anmeldung (NTLM) beim Absenden neu aushandelt und dabei den Inhalt verwirft. Seite neu laden und erneut speichern; dauerhaft hilft der Aufruf über den Servernamen statt über die IP-Adresse (Kerberos) oder ein anderer Browser."),
            (false, _) =>
                ("Der Browser hat das Sicherheits-Cookie nicht mitgeschickt.",
                 "Cookies für diese Adresse im Browser erlauben (auch bei Aufruf über die IP-Adresse) und die Seite neu laden."),
            (_, false) =>
                ("Im Formular fehlt das Sicherheitsfeld.",
                 "Seite neu laden (nicht aus dem Browser-Verlauf wiederherstellen) und erneut speichern."),
            _ when message.Contains("user", StringComparison.OrdinalIgnoreCase) =>
                ("Das Formular wurde für einen anderen Windows-Benutzer erzeugt.",
                 "Seite neu laden, damit das Formular zum angemeldeten Benutzer passt."),
            _ =>
                ("Das Sicherheitsmerkmal des Formulars ist ungültig oder abgelaufen (z.B. nach einer Neuinstallation des Dienstes).",
                 "Seite neu laden und erneut speichern.")
        };

        return new AntiforgeryDiagnosis
        {
            Reason = reason,
            Advice = advice,
            TechnicalMessage = message,
            UserName = context.User.Identity?.Name ?? "(nicht angemeldet)",
            HasCookie = hasCookie,
            HasFormField = hasField,
            ReturnUrl = request.Path + request.QueryString
        };
    }
}
