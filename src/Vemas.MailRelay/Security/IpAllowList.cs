using System.Net;

namespace Vemas.MailRelay.Security;

/// <summary>
/// Prüft Client-IPs gegen eine Liste wie "10.0.0.5; 10.0.1.0/24".
/// Eine leere Liste erlaubt keinen Zugriff.
/// </summary>
public static class IpAllowList
{
    private static readonly char[] Separators = [';', ',', ' ', '\r', '\n'];

    public static bool IsAllowed(string allowList, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(allowList) || address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (string entry in allowList.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseEntry(entry, out var network) && network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Liefert die Einträge, die sich nicht parsen lassen (für die Validierung in der Oberfläche).</summary>
    public static IReadOnlyList<string> GetInvalidEntries(string allowList)
    {
        if (string.IsNullOrWhiteSpace(allowList))
        {
            return [];
        }

        return allowList.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !TryParseEntry(entry, out _))
            .ToList();
    }

    private static bool TryParseEntry(string entry, out IPNetwork network)
    {
        if (entry.Contains('/'))
        {
            return IPNetwork.TryParse(entry, out network);
        }

        if (IPAddress.TryParse(entry, out var single))
        {
            network = new IPNetwork(single, single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        network = default;
        return false;
    }
}
