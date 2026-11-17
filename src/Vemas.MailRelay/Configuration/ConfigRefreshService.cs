using Microsoft.Extensions.Options;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Configuration;

/// <summary>Lädt Profile und SMTP-Benutzer periodisch aus der DB nach (kein Neustart bei Änderungen nötig).</summary>
public sealed class ConfigRefreshService : BackgroundService
{
    private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(15);

    private readonly RelayConfigCache _cache;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<ConfigRefreshService> _logger;

    public ConfigRefreshService(RelayConfigCache cache, IOptions<MaintenanceOptions> options, ILogger<ConfigRefreshService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(_options.ConfigRefreshSeconds, 10));
        bool loaded = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _cache.RefreshAsync(stoppingToken);
                if (!loaded)
                {
                    _logger.LogInformation("Konfiguration geladen: {Count} Profil(e).", _cache.GetProfiles().Count);
                    loaded = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Konfiguration konnte nicht aus der Datenbank geladen werden.");
            }

            await Task.Delay(loaded ? interval : StartupRetryDelay, stoppingToken);
        }
    }
}
