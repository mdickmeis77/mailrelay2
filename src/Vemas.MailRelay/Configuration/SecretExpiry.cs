using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Configuration;

/// <summary>Bewertung des Ablaufdatums eines Client Secrets.</summary>
public static class SecretExpiry
{
    /// <summary>Tage bis zum Ablauf (negativ = abgelaufen) oder null, wenn kein Datum gepflegt ist.</summary>
    public static int? DaysRemaining(RelayProfile profile)
    {
        if (profile.SecretExpiresAt is null)
        {
            return null;
        }

        return (profile.SecretExpiresAt.Value.Date - DateTime.Today).Days;
    }

    public static bool NeedsWarning(RelayProfile profile, int warningDays)
    {
        int? days = DaysRemaining(profile);
        return profile.IsActive && days is not null && days <= warningDays;
    }
}
