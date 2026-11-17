using System.ComponentModel.DataAnnotations;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Web;

/// <summary>Formularmodell für Anlegen/Bearbeiten eines Versandprofils.</summary>
public sealed class ProfileInput
{
    public int ProfileId { get; set; }

    /// <summary>Optional: Kunde, der das Profil in NextGen sieht und bearbeiten darf.</summary>
    public int? CustomerId { get; set; }

    [Required(ErrorMessage = "Name ist erforderlich.")]
    [StringLength(100)]
    public string Name { get; set; }

    [Required(ErrorMessage = "Tenant-ID ist erforderlich.")]
    [StringLength(100)]
    public string TenantId { get; set; }

    [Required(ErrorMessage = "Client-ID (Anwendungs-ID) ist erforderlich.")]
    [StringLength(100)]
    public string ClientId { get; set; }

    /// <summary>Leer lassen, um das vorhandene Secret zu behalten.</summary>
    [StringLength(500)]
    public string NewSecret { get; set; }

    [DataType(DataType.Date)]
    public DateTime? SecretExpiresAt { get; set; }

    public SenderMode SenderMode { get; set; } = SenderMode.FixedSender;

    [Required(ErrorMessage = "Standard-Absender ist erforderlich.")]
    [EmailAddress(ErrorMessage = "Standard-Absender ist keine gültige Adresse.")]
    [StringLength(320)]
    public string DefaultSender { get; set; }

    /// <summary>Eine Adresse oder *@domain.de pro Zeile.</summary>
    public string AllowedSenders { get; set; }

    public bool IsActive { get; set; } = true;

    public static ProfileInput FromProfile(RelayProfile profile)
    {
        return new ProfileInput
        {
            ProfileId = profile.ProfileId,
            CustomerId = profile.CustomerId,
            Name = profile.Name,
            TenantId = profile.TenantId,
            ClientId = profile.ClientId,
            SecretExpiresAt = profile.SecretExpiresAt,
            SenderMode = profile.SenderMode,
            DefaultSender = profile.DefaultSender,
            AllowedSenders = string.Join(Environment.NewLine, profile.AllowedSenders),
            IsActive = profile.IsActive
        };
    }

    public RelayProfile ToProfile()
    {
        return new RelayProfile
        {
            ProfileId = ProfileId,
            CustomerId = CustomerId,
            Name = Name,
            TenantId = TenantId,
            ClientId = ClientId,
            SecretExpiresAt = SecretExpiresAt,
            SenderMode = SenderMode,
            DefaultSender = DefaultSender,
            IsActive = IsActive,
            AllowedSenders = GetAllowedSenderList()
        };
    }

    public List<string> GetAllowedSenderList()
    {
        return (AllowedSenders ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
