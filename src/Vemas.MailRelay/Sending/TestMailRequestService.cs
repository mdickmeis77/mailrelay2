using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;

namespace Vemas.MailRelay.Sending;

/// <summary>Arbeitet Testmail-Aufträge aus NextGen ab (mailrelay.TestMailRequest).</summary>
public sealed class TestMailRequestService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly TestMailRequestRepository _repository;
    private readonly TestMailService _testMailService;
    private readonly RelayConfigCache _cache;
    private readonly ILogger<TestMailRequestService> _logger;

    public TestMailRequestService(TestMailRequestRepository repository, TestMailService testMailService,
        RelayConfigCache cache, ILogger<TestMailRequestService> logger)
    {
        _repository = repository;
        _testMailService = testMailService;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _cache.WaitForInitialLoadAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Testmail-Aufträge konnten nicht verarbeitet werden.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var requests = await _repository.ClaimPendingAsync(cancellationToken);
        if (requests.Count == 0)
        {
            return;
        }

        // Profile könnten gerade erst angelegt oder geändert worden sein.
        await _cache.RefreshAsync(cancellationToken);

        foreach (var request in requests)
        {
            var profile = _cache.GetProfile(request.ProfileId);
            if (profile is null || !profile.IsActive)
            {
                await _repository.CompleteAsync(request.TestMailRequestId, null, "Profil nicht gefunden oder deaktiviert.", cancellationToken);
                continue;
            }

            try
            {
                long messageId = await _testMailService.EnqueueAsync(profile, request.Recipient, request.RequestedBy, cancellationToken);
                await _repository.CompleteAsync(request.TestMailRequestId, messageId, null, cancellationToken);
            }
            catch (FormatException ex)
            {
                // MimeKit.ParseException leitet von FormatException ab (ungültige Empfängeradresse).
                await _repository.CompleteAsync(request.TestMailRequestId, null, $"Ungültige Adresse: {ex.Message}", cancellationToken);
            }
        }
    }
}
