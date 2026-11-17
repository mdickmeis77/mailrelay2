using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Configuration;

/// <summary>
/// Hält Profile und SMTP-Benutzer im Speicher, damit SMTP-Anmeldung und Versand
/// nicht bei jeder Mail die DB abfragen. Wird periodisch und nach Änderungen neu geladen.
/// </summary>
public sealed class RelayConfigCache
{
    private readonly ProfileRepository _profileRepository;
    private readonly SmtpUserRepository _smtpUserRepository;
    private readonly TaskCompletionSource _initialLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private volatile Snapshot _snapshot = new(
        new Dictionary<int, RelayProfile>(),
        new Dictionary<string, RelaySmtpUser>(StringComparer.OrdinalIgnoreCase));

    public RelayConfigCache(ProfileRepository profileRepository, SmtpUserRepository smtpUserRepository)
    {
        _profileRepository = profileRepository;
        _smtpUserRepository = smtpUserRepository;
    }

    /// <summary>Wartet, bis die Konfiguration einmal erfolgreich geladen wurde.</summary>
    public Task WaitForInitialLoadAsync(CancellationToken cancellationToken)
    {
        return _initialLoad.Task.WaitAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await _profileRepository.GetAllAsync(cancellationToken);
            var users = await _smtpUserRepository.GetAllAsync(cancellationToken);

            _snapshot = new Snapshot(
                profiles.ToDictionary(p => p.ProfileId),
                users.ToDictionary(u => u.Login, StringComparer.OrdinalIgnoreCase));

            _initialLoad.TrySetResult();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public RelaySmtpUser FindUser(string login)
    {
        return login is not null && _snapshot.Users.TryGetValue(login, out var user) ? user : null;
    }

    public RelayProfile GetProfile(int profileId)
    {
        return _snapshot.Profiles.TryGetValue(profileId, out var profile) ? profile : null;
    }

    public IReadOnlyCollection<RelayProfile> GetProfiles()
    {
        return _snapshot.Profiles.Values.ToList();
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<int, RelayProfile> Profiles,
        IReadOnlyDictionary<string, RelaySmtpUser> Users);
}
