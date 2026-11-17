using Microsoft.Identity.Client;
using System.Collections.Concurrent;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Security;

namespace Vemas.MailRelay.Sending;

/// <summary>
/// Holt App-Tokens (Client Credentials) für Microsoft Graph. Je Profil wird eine MSAL-Instanz
/// gehalten, deren Token-Cache das Token bis kurz vor Ablauf wiederverwendet.
/// </summary>
public sealed class GraphTokenProvider
{
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    private readonly MsalHttpClientFactoryAdapter _httpClientFactory;
    private readonly ConcurrentDictionary<string, IConfidentialClientApplication> _applications = new();

    public GraphTokenProvider(MsalHttpClientFactoryAdapter httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetAccessTokenAsync(RelayProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.HasSecret)
        {
            throw new PermanentSendException($"Für Profil '{profile.Name}' ist kein Client Secret hinterlegt.");
        }

        // Schlüssel enthält ModifiedAt: nach Änderung von Secret/Tenant/Client wird eine neue Instanz erzeugt.
        string key = $"{profile.ProfileId}:{profile.ModifiedAt.Ticks}";
        if (!_applications.ContainsKey(key))
        {
            RemoveOutdated(profile.ProfileId);
        }

        var application = _applications.GetOrAdd(key, _ => CreateApplication(profile));
        var result = await application.AcquireTokenForClient(GraphScopes).ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    private IConfidentialClientApplication CreateApplication(RelayProfile profile)
    {
        return ConfidentialClientApplicationBuilder
            .Create(profile.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{profile.TenantId}")
            .WithClientSecret(SecretProtector.Unprotect(profile.SecretEncrypted))
            .WithHttpClientFactory(_httpClientFactory)
            .Build();
    }

    private void RemoveOutdated(int profileId)
    {
        string prefix = $"{profileId}:";
        foreach (string key in _applications.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _applications.TryRemove(key, out _);
        }
    }
}
