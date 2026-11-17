using SmtpServer;
using SmtpServer.Authentication;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Security;

namespace Vemas.MailRelay.Smtp;

/// <summary>Prüft AUTH LOGIN/PLAIN gegen die SMTP-Benutzer (Passwort-Hash, IP-Allowlist, aktives Profil).</summary>
public sealed class RelayUserAuthenticator : IUserAuthenticator
{
    private readonly RelayConfigCache _cache;
    private readonly SmtpUserRepository _smtpUserRepository;
    private readonly ILogger<RelayUserAuthenticator> _logger;

    public RelayUserAuthenticator(RelayConfigCache cache, SmtpUserRepository smtpUserRepository, ILogger<RelayUserAuthenticator> logger)
    {
        _cache = cache;
        _smtpUserRepository = smtpUserRepository;
        _logger = logger;
    }

    public async Task<bool> AuthenticateAsync(ISessionContext context, string user, string password, CancellationToken cancellationToken)
    {
        var remoteAddress = context.GetRemoteAddress();
        var smtpUser = _cache.FindUser(user);

        string denyReason = GetDenyReason(smtpUser, password, remoteAddress);
        if (denyReason is not null)
        {
            _logger.LogWarning("SMTP-Anmeldung abgelehnt: Benutzer '{User}' von {Ip}: {Reason}", user, remoteAddress, denyReason);
            return false;
        }

        context.SetSmtpUserId(smtpUser.SmtpUserId);
        await TryUpdateLastLoginAsync(smtpUser.SmtpUserId, cancellationToken);
        return true;
    }

    private string GetDenyReason(Domain.RelaySmtpUser smtpUser, string password, System.Net.IPAddress remoteAddress)
    {
        if (smtpUser is null)
        {
            return "Benutzer unbekannt";
        }

        if (!smtpUser.IsActive)
        {
            return "Benutzer deaktiviert";
        }

        if (!IpAllowList.IsAllowed(smtpUser.AllowedIps, remoteAddress))
        {
            return "IP-Adresse nicht freigegeben";
        }

        var profile = _cache.GetProfile(smtpUser.ProfileId);
        if (profile is null || !profile.IsActive)
        {
            return "Profil nicht vorhanden oder deaktiviert";
        }

        if (!PasswordHasher.Verify(password, smtpUser.PasswordHash))
        {
            return "Passwort falsch";
        }

        return null;
    }

    private async Task TryUpdateLastLoginAsync(int smtpUserId, CancellationToken cancellationToken)
    {
        try
        {
            await _smtpUserRepository.UpdateLastLoginAsync(smtpUserId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nur Statistik: Anmeldung soll daran nicht scheitern.
            _logger.LogWarning(ex, "LastLoginAt für SMTP-Benutzer {Id} konnte nicht gespeichert werden.", smtpUserId);
        }
    }
}
