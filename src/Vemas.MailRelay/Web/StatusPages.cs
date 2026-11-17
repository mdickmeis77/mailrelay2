using Microsoft.AspNetCore.Diagnostics;

namespace Vemas.MailRelay.Web;

/// <summary>
/// Verständliche Antworten für Fehlerstatus der Weboberfläche. Header (z.B. WWW-Authenticate bei 401)
/// bleiben unverändert, es wird nur der Inhalt geschrieben.
/// </summary>
public static class StatusPages
{
    private const string BadRequestHtml = """
        <!DOCTYPE html>
        <html lang="de"><head><meta charset="utf-8" /><title>Formular abgelaufen – Vemas.MailRelay</title>
        <style>body{font:15px/1.5 "Segoe UI",system-ui,sans-serif;max-width:640px;margin:48px auto;padding:0 16px;color:#1d2330}
        a{color:#1f5fa8}</style></head>
        <body>
        <h1>Formular konnte nicht gespeichert werden</h1>
        <p>Das Formular ist abgelaufen oder wurde in einem anderen Fenster neu geladen
        (Schutz gegen Fälschung von Formularen, HTTP 400).</p>
        <p>Bitte die Seite <strong>neu laden</strong> und die Eingaben erneut speichern.
        Tritt der Fehler dauerhaft auf: Cookies für diese Seite im Browser erlauben.
        Der genaue Grund steht in der Windows-Ereignisanzeige (Quelle Vemas.MailRelay).</p>
        <p><a href="javascript:history.back()">Zurück zum Formular</a> · <a href="/">Zum Dashboard</a></p>
        </body></html>
        """;

    public static async Task WriteAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        if (response.StatusCode == StatusCodes.Status400BadRequest)
        {
            response.ContentType = "text/html; charset=utf-8";
            await response.WriteAsync(BadRequestHtml);
            return;
        }

        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync($"Status Code: {response.StatusCode}");
    }
}
