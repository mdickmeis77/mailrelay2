namespace Vemas.MailRelay.Domain;

/// <summary>Per SQL eingetragenes Klartext-Secret bzw. -Passwort, das noch verarbeitet werden muss.</summary>
public sealed class PendingPlaintext
{
    public int Id { get; set; }
    public string Plaintext { get; set; } = string.Empty;
}
