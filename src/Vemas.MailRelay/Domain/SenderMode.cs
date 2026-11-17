namespace Vemas.MailRelay.Domain;

/// <summary>Wie der Absender beim Versand bestimmt wird.</summary>
public enum SenderMode
{
    /// <summary>Immer das Profil-Postfach; ursprünglicher Absender wird Reply-To.</summary>
    FixedSender,

    /// <summary>From aus der Mail wird übernommen, sofern erlaubt.</summary>
    SendAs
}
