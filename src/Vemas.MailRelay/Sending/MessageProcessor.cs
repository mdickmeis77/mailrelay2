using Microsoft.Extensions.Options;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Sending;

/// <summary>Versendet eine reservierte Mail und schreibt das Ergebnis (Sent / Retry / Failed) ins Protokoll.</summary>
public sealed class MessageProcessor
{
    private readonly RelayConfigCache _cache;
    private readonly GraphMailSender _sender;
    private readonly MessageQueueRepository _queueRepository;
    private readonly DatabaseMailFeedback _databaseMailFeedback;
    private readonly SendingOptions _options;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(RelayConfigCache cache, GraphMailSender sender, MessageQueueRepository queueRepository,
        DatabaseMailFeedback databaseMailFeedback, IOptions<SendingOptions> options, ILogger<MessageProcessor> logger)
    {
        _cache = cache;
        _sender = sender;
        _queueRepository = queueRepository;
        _databaseMailFeedback = databaseMailFeedback;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(QueuedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var profile = _cache.GetProfile(message.ProfileId)
                ?? throw new PermanentSendException($"Profil {message.ProfileId} existiert nicht mehr.");

            var response = await _sender.SendAsync(message, profile, cancellationToken);

            // Ergebnis auch beim Herunterfahren speichern, sonst würde die Mail doppelt versendet.
            await _queueRepository.MarkSentAsync(message.MessageId, response.HttpStatus, response.RequestId, CancellationToken.None);
            _logger.LogDebug("Mail {Id} über Profil '{Profile}' versendet.", message.MessageId, profile.Name);
            await _databaseMailFeedback.ReportSentAsync(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Dienst wird beendet: Mail bleibt "Sending" und wird nach Ablauf der Sperre erneut aufgenommen.
            throw;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(message, ex);
        }
    }

    private async Task HandleFailureAsync(QueuedMessage message, Exception exception)
    {
        var error = SendErrorClassifier.Classify(exception);
        int attemptNumber = message.AttemptCount + 1;
        var retryDelays = _options.GetRetryDelays();

        try
        {
            if (error.IsTransient && attemptNumber <= retryDelays.Count)
            {
                var nextAttempt = DateTime.UtcNow.AddMinutes(retryDelays[attemptNumber - 1]);
                await _queueRepository.MarkRetryAsync(message.MessageId, nextAttempt, error.HttpStatus, error.Message, error.RequestId, CancellationToken.None);
                _logger.LogWarning("Mail {Id}: Versuch {Attempt} fehlgeschlagen, nächster Versuch {Next:HH:mm} UTC. {Error}",
                    message.MessageId, attemptNumber, nextAttempt, error.Message);
            }
            else
            {
                await _queueRepository.MarkFailedAsync(message.MessageId, error.HttpStatus, error.Message, error.RequestId, CancellationToken.None);
                _logger.LogError(exception, "Mail {Id} endgültig fehlgeschlagen nach {Attempt} Versuch(en): {Error}",
                    message.MessageId, attemptNumber, error.Message);
                await _databaseMailFeedback.ReportFailedAsync(message, error.Message);
            }
        }
        catch (Exception dbException)
        {
            _logger.LogError(dbException, "Ergebnis für Mail {Id} konnte nicht gespeichert werden. Ursprünglicher Fehler: {Error}",
                message.MessageId, error.Message);
        }
    }
}
