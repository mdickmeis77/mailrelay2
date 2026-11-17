using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Sending;

namespace Vemas.MailRelay.Smtp;

/// <summary>
/// Speichert eingehende Mails unverändert in der Queue. Erst nach erfolgreichem Speichern
/// erhält der Client "250 OK"; bei DB-Fehlern "451", damit SQL Server / SSRS es erneut versuchen.
/// </summary>
public sealed class RelayMessageStore : IMessageStore
{
    private readonly RelayConfigCache _cache;
    private readonly MessageQueueRepository _queueRepository;
    private readonly QueueSignal _queueSignal;
    private readonly ILogger<RelayMessageStore> _logger;

    public RelayMessageStore(RelayConfigCache cache, MessageQueueRepository queueRepository, QueueSignal queueSignal, ILogger<RelayMessageStore> logger)
    {
        _cache = cache;
        _queueRepository = queueRepository;
        _queueSignal = queueSignal;
        _logger = logger;
    }

    public async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        var smtpUser = _cache.FindUser(context.Authentication.User);
        var profile = smtpUser is null ? null : _cache.GetProfile(smtpUser.ProfileId);
        if (profile is null)
        {
            return new SmtpResponse(SmtpReplyCode.AuthenticationRequired, "5.7.0 Anmeldung erforderlich");
        }

        byte[] mime = buffer.ToArray();
        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(mime, writable: false);
            message = await MimeMessage.LoadAsync(stream, cancellationToken);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Mail von '{User}' ist kein gültiges MIME und wurde abgelehnt.", smtpUser.Login);
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "5.6.0 Mail nicht lesbar");
        }

        string headerFrom = message.From.Mailboxes.FirstOrDefault()?.Address;
        if (!SenderPolicy.IsAllowed(profile, headerFrom))
        {
            _logger.LogWarning("Absender '{From}' (Header) für Profil '{Profile}' nicht erlaubt.", headerFrom, profile.Name);
            return new SmtpResponse(SmtpReplyCode.MailboxUnavailable, "5.7.1 Absender nicht erlaubt");
        }

        var newMessage = new NewMessage
        {
            SmtpUserId = smtpUser.SmtpUserId,
            ProfileId = profile.ProfileId,
            ClientIp = context.GetRemoteAddress()?.ToString(),
            EnvelopeFrom = transaction.From.AsAddressString(),
            EnvelopeTo = transaction.To.Select(m => m.AsAddressString()).ToList(),
            HeaderFrom = headerFrom,
            HeaderTo = string.Join("; ", message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(m => m.Address)),
            Subject = message.Subject,
            Mime = mime
        };

        try
        {
            long messageId = await _queueRepository.InsertAsync(newMessage, cancellationToken);
            _queueSignal.Notify();
            _logger.LogDebug("Mail {Id} von '{User}' angenommen ({Size} Bytes).", messageId, smtpUser.Login, mime.Length);
            return SmtpResponse.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Mail von '{User}' konnte nicht in die Queue geschrieben werden.", smtpUser.Login);
            return new SmtpResponse(SmtpReplyCode.Aborted, "4.3.0 Zwischenspeicherung fehlgeschlagen, bitte erneut versuchen");
        }
    }
}
