namespace Vemas.MailRelay.Domain;

/// <summary>SMTP-Benutzer (Basic Auth) für SQL Server / SSRS.</summary>
public sealed class RelaySmtpUser
{
    public int SmtpUserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; }
    public int ProfileId { get; set; }
    public string AllowedIps { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
}
