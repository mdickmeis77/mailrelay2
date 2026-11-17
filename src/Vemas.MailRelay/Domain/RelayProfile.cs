namespace Vemas.MailRelay.Domain;

/// <summary>Versandprofil: App-Registrierung in einem Tenant.</summary>
public sealed class RelayProfile
{
    public int ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public byte[] SecretEncrypted { get; set; }
    public DateTime? SecretExpiresAt { get; set; }
    public SenderMode SenderMode { get; set; } = SenderMode.FixedSender;
    public string DefaultSender { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime ModifiedAt { get; set; }
    public List<string> AllowedSenders { get; set; } = [];

    public bool HasSecret => SecretEncrypted is { Length: > 0 };
}
