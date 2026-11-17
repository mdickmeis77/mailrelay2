using Microsoft.Extensions.Options;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Options;
using Vemas.MailRelay.Security;

namespace Vemas.MailRelay.Maintenance;

/// <summary>
/// Hintergrundaufgaben: per SQL eingetragene Klartext-Secrets/Passwörter verschlüsseln bzw. hashen (minütlich),
/// Aufräumen nach Aufbewahrungsfristen und Warnung vor ablaufenden Secrets (täglich).
/// </summary>
public sealed class MaintenanceService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly MaintenanceRepository _repository;
    private readonly RelayConfigCache _cache;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceService> _logger;
    private DateTime _lastDailyRun = DateTime.MinValue;

    public MaintenanceService(MaintenanceRepository repository, RelayConfigCache cache,
        IOptions<MaintenanceOptions> options, ILogger<MaintenanceService> logger)
    {
        _repository = repository;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _cache.WaitForInitialLoadAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSafeAsync("Klartextfelder verarbeiten", ProcessPlaintextFieldsAsync, stoppingToken);

            if (_lastDailyRun != DateTime.Today)
            {
                await RunSafeAsync("Aufräumen", CleanupAsync, stoppingToken);
                WarnAboutExpiringSecrets();
                _lastDailyRun = DateTime.Today;
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessPlaintextFieldsAsync(CancellationToken cancellationToken)
    {
        int changed = 0;

        foreach (var pending in await _repository.GetPendingSecretsAsync(cancellationToken))
        {
            await _repository.StoreSecretAsync(pending.Id, SecretProtector.Protect(pending.Plaintext), cancellationToken);
            _logger.LogInformation("Client Secret für Profil {Id} aus Klartextfeld übernommen und verschlüsselt.", pending.Id);
            changed++;
        }

        foreach (var pending in await _repository.GetPendingPasswordsAsync(cancellationToken))
        {
            await _repository.StorePasswordHashAsync(pending.Id, PasswordHasher.Hash(pending.Plaintext), cancellationToken);
            _logger.LogInformation("Passwort für SMTP-Benutzer {Id} aus Klartextfeld übernommen und gehasht.", pending.Id);
            changed++;
        }

        if (changed > 0)
        {
            await _cache.RefreshAsync(cancellationToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await _repository.CleanupAsync(_options.MimeRetentionDays, _options.LogRetentionDays, cancellationToken);
        _logger.LogInformation("Aufräumen abgeschlossen (MIME > {Mime} Tage, Protokoll > {Log} Tage).",
            _options.MimeRetentionDays, _options.LogRetentionDays);
    }

    private void WarnAboutExpiringSecrets()
    {
        foreach (var profile in _cache.GetProfiles().Where(p => SecretExpiry.NeedsWarning(p, _options.SecretExpiryWarningDays)))
        {
            int days = SecretExpiry.DaysRemaining(profile) ?? 0;
            if (days < 0)
            {
                _logger.LogError("Client Secret von Profil '{Profile}' ist seit {Date:dd.MM.yyyy} abgelaufen!",
                    profile.Name, profile.SecretExpiresAt);
            }
            else
            {
                _logger.LogWarning("Client Secret von Profil '{Profile}' läuft in {Days} Tag(en) ab ({Date:dd.MM.yyyy}).",
                    profile.Name, days, profile.SecretExpiresAt);
            }
        }
    }

    private async Task RunSafeAsync(string taskName, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Wartungsaufgabe '{Task}' fehlgeschlagen.", taskName);
        }
    }
}
