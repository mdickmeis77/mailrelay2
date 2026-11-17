using MimeKit;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Sending;

/// <summary>Stellt eine Testmail für ein Profil in die Queue (durchläuft denselben Weg wie echte Mails).</summary>
public sealed class TestMailService
{
    private const string ClientIpMarker = "Web";

    private readonly MessageQueueRepository _queueRepository;
    private readonly QueueSignal _queueSignal;

    public TestMailService(MessageQueueRepository queueRepository, QueueSignal queueSignal)
    {
        _queueRepository = queueRepository;
        _queueSignal = queueSignal;
    }

    public async Task<long> EnqueueAsync(RelayProfile profile, string recipient, string requestedBy, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(profile.DefaultSender));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = $"Vemas.MailRelay Testmail - Profil {profile.Name}";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                Diese Testmail wurde über Vemas.MailRelay versendet.

                Profil:       {profile.Name}
                Tenant:       {profile.TenantId}
                Absender:     {profile.DefaultSender}
                Angefordert:  {requestedBy}
                Zeitpunkt:    {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                """
        };

        byte[] mime = await MimeSerializer.SerializeAsync(message, cancellationToken);
        long messageId = await _queueRepository.InsertAsync(new NewMessage
        {
            ProfileId = profile.ProfileId,
            ClientIp = ClientIpMarker,
            EnvelopeFrom = profile.DefaultSender,
            EnvelopeTo = [recipient],
            HeaderFrom = profile.DefaultSender,
            HeaderTo = recipient,
            Subject = message.Subject,
            Mime = mime,
            Content = MessageContentExtractor.Extract(message)
        }, cancellationToken);

        _queueSignal.Notify();
        return messageId;
    }
}
