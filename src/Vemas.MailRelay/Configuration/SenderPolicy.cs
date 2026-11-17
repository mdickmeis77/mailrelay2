using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Configuration;

/// <summary>Regeln, welche Absender ein Profil verwenden darf.</summary>
public static class SenderPolicy
{
    private const string DomainWildcardPrefix = "*@";

    /// <summary>
    /// FixedSender: jeder Absender wird angenommen (er wird beim Versand ohnehin ersetzt).
    /// SendAs: Absender muss dem Standard-Absender oder einem Eintrag der Liste entsprechen.
    /// </summary>
    public static bool IsAllowed(RelayProfile profile, string address)
    {
        if (profile.SenderMode == SenderMode.FixedSender)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        address = address.Trim();
        if (string.Equals(address, profile.DefaultSender, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string pattern in profile.AllowedSenders)
        {
            if (pattern.StartsWith(DomainWildcardPrefix, StringComparison.Ordinal))
            {
                if (address.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(address, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
