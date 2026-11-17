using System.ComponentModel.DataAnnotations;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Web;

/// <summary>Formularmodell für Anlegen/Bearbeiten eines SMTP-Benutzers.</summary>
public sealed class SmtpUserInput
{
    public int SmtpUserId { get; set; }

    [Required(ErrorMessage = "Login ist erforderlich.")]
    [StringLength(100)]
    public string Login { get; set; }

    /// <summary>Leer lassen, um das vorhandene Passwort zu behalten.</summary>
    [StringLength(200, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen lang sein.")]
    public string NewPassword { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Bitte ein Profil wählen.")]
    public int ProfileId { get; set; }

    [StringLength(1000)]
    public string AllowedIps { get; set; }

    public bool IsActive { get; set; } = true;

    public static SmtpUserInput FromUser(RelaySmtpUser user)
    {
        return new SmtpUserInput
        {
            SmtpUserId = user.SmtpUserId,
            Login = user.Login,
            ProfileId = user.ProfileId,
            AllowedIps = user.AllowedIps,
            IsActive = user.IsActive
        };
    }

    public RelaySmtpUser ToUser()
    {
        return new RelaySmtpUser
        {
            SmtpUserId = SmtpUserId,
            Login = Login,
            ProfileId = ProfileId,
            AllowedIps = AllowedIps,
            IsActive = IsActive
        };
    }
}
