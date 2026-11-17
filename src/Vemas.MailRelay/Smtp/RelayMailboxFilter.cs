using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using Vemas.MailRelay.Configuration;

namespace Vemas.MailRelay.Smtp;

/// <summary>Prüft bei MAIL FROM, ob der Envelope-Absender für das Profil des angemeldeten Benutzers erlaubt ist.</summary>
public sealed class RelayMailboxFilter : IMailboxFilter
{
    private readonly RelayConfigCache _cache;
    private readonly ILogger<RelayMailboxFilter> _logger;

    public RelayMailboxFilter(RelayConfigCache cache, ILogger<RelayMailboxFilter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<bool> CanAcceptFromAsync(ISessionContext context, IMailbox from, int size, CancellationToken cancellationToken)
    {
        var smtpUser = _cache.FindUser(context.Authentication.User);
        var profile = smtpUser is null ? null : _cache.GetProfile(smtpUser.ProfileId);
        if (profile is null)
        {
            throw new SmtpResponseException(new SmtpResponse(SmtpReplyCode.AuthenticationRequired, "5.7.0 Anmeldung erforderlich"));
        }

        string address = from.AsAddressString();
        if (!SenderPolicy.IsAllowed(profile, address))
        {
            _logger.LogWarning("Absender '{From}' für Profil '{Profile}' nicht erlaubt (Benutzer '{User}').",
                address, profile.Name, smtpUser.Login);
            throw new SmtpResponseException(new SmtpResponse(SmtpReplyCode.MailboxUnavailable, "5.7.1 Absender nicht erlaubt"));
        }

        return Task.FromResult(true);
    }

    public Task<bool> CanDeliverToAsync(ISessionContext context, IMailbox to, IMailbox from, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}
