using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Sending;

/// <summary>
/// Meldet endgültig fehlgeschlagene Mails an SQL Server Database Mail zurück und setzt den Status nach
/// erfolgreichem Neu senden wieder auf "gesendet". Fehler hier beeinflussen den Versand nie.
/// </summary>
public sealed class DatabaseMailFeedback
{
    // Prozedur fehlt, kein Recht, Datenbank/Anmeldung nicht möglich: Einrichtungsproblem, nur gedrosselt melden
    private static readonly HashSet<int> SetupErrors = [2812, 229, 916, 4060, 18456];
    private static readonly TimeSpan SetupWarningInterval = TimeSpan.FromHours(1);

    private readonly DatabaseMailRepository _databaseMail;
    private readonly MessageQueueRepository _queueRepository;
    private readonly DatabaseMailOptions _options;
    private readonly ILogger<DatabaseMailFeedback> _logger;
    private DateTime _lastSetupWarningUtc = DateTime.MinValue;

    public DatabaseMailFeedback(DatabaseMailRepository databaseMail, MessageQueueRepository queueRepository,
        IOptions<DatabaseMailOptions> options, ILogger<DatabaseMailFeedback> logger)
    {
        _databaseMail = databaseMail;
        _queueRepository = queueRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ReportFailedAsync(QueuedMessage message, string errorText)
    {
        if (!_options.Enabled || message.DbMailItemId is not null)
        {
            return;
        }

        try
        {
            int mailItemId = await _databaseMail.ReportFailedAsync(message.MessageId, message.Subject, message.EnvelopeTo,
                message.ReceivedAtUtc, errorText, CancellationToken.None);
            if (mailItemId > 0)
            {
                await _queueRepository.SaveDatabaseMailFeedbackAsync(message.MessageId, mailItemId,
                    $"Database-Mail Nr. {mailItemId} in msdb als fehlgeschlagen gemeldet.", CancellationToken.None);
                _logger.LogInformation("Mail {Id}: Fehler an Database Mail gemeldet (mailitem_id {MailItemId}).", message.MessageId, mailItemId);
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, message.MessageId);
        }
    }

    public async Task ReportSentAsync(QueuedMessage message)
    {
        if (!_options.Enabled || message.DbMailItemId is not int mailItemId)
        {
            return;
        }

        try
        {
            if (await _databaseMail.ReportSentAsync(message.MessageId, mailItemId, CancellationToken.None) > 0)
            {
                await _queueRepository.SaveDatabaseMailFeedbackAsync(message.MessageId, mailItemId,
                    $"Database-Mail Nr. {mailItemId} in msdb wieder als gesendet gemeldet.", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, message.MessageId);
        }
    }

    private void LogFailure(Exception exception, long messageId)
    {
        if (exception is SqlException sql && SetupErrors.Contains(sql.Number))
        {
            if (DateTime.UtcNow - _lastSetupWarningUtc < SetupWarningInterval)
            {
                return;
            }

            _lastSetupWarningUtc = DateTime.UtcNow;
            _logger.LogWarning("Rückmeldung an Database Mail nicht möglich: {Error} Install-DatabaseMail-Rueckmeldung.sql ausführen " +
                "oder die Rückmeldung mit DatabaseMail:Enabled = false abschalten. (Hinweis höchstens einmal pro Stunde)", sql.Message);
            return;
        }

        _logger.LogWarning(exception, "Mail {Id}: Rückmeldung an Database Mail fehlgeschlagen.", messageId);
    }
}
