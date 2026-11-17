using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Sending;

/// <summary>Versendet eine Mail aus der Queue über Microsoft Graph im Postfach des Absenders.</summary>
public sealed class GraphMailSender
{
    private readonly GraphTokenProvider _tokenProvider;
    private readonly GraphMailClient _graphClient;
    private readonly LargeMessageSender _largeMessageSender;

    public GraphMailSender(GraphTokenProvider tokenProvider, GraphMailClient graphClient, LargeMessageSender largeMessageSender)
    {
        _tokenProvider = tokenProvider;
        _graphClient = graphClient;
        _largeMessageSender = largeMessageSender;
    }

    public async Task<GraphResponseInfo> SendAsync(QueuedMessage queued, RelayProfile profile, CancellationToken cancellationToken)
    {
        if (queued.Mime is null)
        {
            throw new PermanentSendException("MIME-Inhalt nicht mehr vorhanden (Aufbewahrungsfrist abgelaufen).");
        }

        if (!profile.IsActive)
        {
            throw new PermanentSendException($"Profil '{profile.Name}' ist deaktiviert.");
        }

        var message = await MimeSerializer.LoadAsync(queued.Mime, cancellationToken);
        string sender = MessagePreparer.Prepare(message, profile, queued.GetEnvelopeRecipients());
        byte[] mime = await MimeSerializer.SerializeAsync(message, cancellationToken);

        string token = await _tokenProvider.GetAccessTokenAsync(profile, cancellationToken);

        if (MimeSerializer.FitsIntoSingleRequest(mime.Length))
        {
            return await _graphClient.SendMimeAsync(token, sender, mime, cancellationToken);
        }

        return await _largeMessageSender.SendAsync(token, sender, message, cancellationToken);
    }
}
