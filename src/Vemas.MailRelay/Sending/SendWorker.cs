using Microsoft.Extensions.Options;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Sending;

/// <summary>Arbeitet die Queue ab: fällige Mails reservieren und parallel versenden.</summary>
public sealed class SendWorker : BackgroundService
{
    private readonly RelayConfigCache _cache;
    private readonly MessageQueueRepository _queueRepository;
    private readonly MessageProcessor _processor;
    private readonly QueueSignal _queueSignal;
    private readonly SendingOptions _options;
    private readonly ILogger<SendWorker> _logger;

    public SendWorker(RelayConfigCache cache, MessageQueueRepository queueRepository, MessageProcessor processor,
        QueueSignal queueSignal, IOptions<SendingOptions> options, ILogger<SendWorker> logger)
    {
        _cache = cache;
        _queueRepository = queueRepository;
        _processor = processor;
        _queueSignal = queueSignal;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _cache.WaitForInitialLoadAsync(stoppingToken);

        int maxParallel = Math.Max(1, _options.MaxParallel);
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = stoppingToken };

        _logger.LogInformation("Versand gestartet (parallel: {Parallel}, Wiederholungen nach {Delays} Minuten).",
            maxParallel, string.Join(", ", _options.GetRetryDelays()));

        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed = 0;
            try
            {
                var batch = await _queueRepository.ClaimBatchAsync(maxParallel * 2, stoppingToken);
                claimed = batch.Count;
                await Parallel.ForEachAsync(batch, parallelOptions, async (message, token) => await _processor.ProcessAsync(message, token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Fehler beim Abarbeiten der Queue.");
                claimed = 0;
            }

            // Volle Charge: sofort weitermachen. Sonst auf neue Mail oder Poll-Intervall (fällige Retries) warten.
            if (claimed < maxParallel * 2)
            {
                await _queueSignal.WaitAsync(pollInterval, stoppingToken);
            }
        }
    }
}
