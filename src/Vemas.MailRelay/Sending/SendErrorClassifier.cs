using Microsoft.Identity.Client;
using System.Security.Cryptography;

namespace Vemas.MailRelay.Sending;

/// <summary>Entscheidet, ob ein Versandfehler wiederholt werden soll, und erzeugt einen lesbaren Fehlertext.</summary>
public static class SendErrorClassifier
{
    private const int TooManyRequests = 429;
    private const int FirstServerError = 500;

    public static SendError Classify(Exception exception)
    {
        return exception switch
        {
            PermanentSendException ex => new SendError(false, null, ex.Message, null),
            GraphRequestException ex => new SendError(ex.IsTransient, (int)ex.StatusCode, ex.Message, ex.RequestId),
            MsalServiceException ex => ClassifyMsalService(ex),
            MsalClientException ex => new SendError(ex.InnerException is HttpRequestException, null,
                $"Anmeldung bei Entra ID fehlgeschlagen ({ex.ErrorCode}): {FirstLine(ex.Message)}", null),
            CryptographicException => new SendError(false, null,
                "Client Secret kann auf diesem Server nicht entschlüsselt werden - bitte im Profil neu eintragen.", null),
            FormatException ex => new SendError(false, null, $"Mail nicht lesbar: {ex.Message}", null),
            HttpRequestException ex => new SendError(true, (int?)ex.StatusCode, $"Netzwerkfehler: {ex.Message}", null),
            OperationCanceledException => new SendError(true, null, "Zeitüberschreitung bei Microsoft Graph.", null),
            _ => new SendError(true, null, $"{exception.GetType().Name}: {exception.Message}", null)
        };
    }

    private static SendError ClassifyMsalService(MsalServiceException ex)
    {
        bool transient = ex.IsRetryable || ex.StatusCode == TooManyRequests || ex.StatusCode >= FirstServerError;
        return new SendError(transient, ex.StatusCode,
            $"Anmeldung bei Entra ID fehlgeschlagen ({ex.ErrorCode}): {FirstLine(ex.Message)}", ex.CorrelationId);
    }

    private static string FirstLine(string text)
    {
        int index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }
}
