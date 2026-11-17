namespace Vemas.MailRelay.Sending;

/// <summary>Fehler, bei dem eine Wiederholung sinnlos ist (z.B. Profil deaktiviert, Absender nicht erlaubt).</summary>
public sealed class PermanentSendException : Exception
{
    public PermanentSendException(string message)
        : base(message)
    {
    }
}
