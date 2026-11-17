using Microsoft.Extensions.Options;
using SmtpServer;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Options;
using Vemas.MailRelay.Security;

namespace Vemas.MailRelay.Smtp;

/// <summary>Betreibt den SMTP-Eingang (Port 587, AUTH LOGIN/PLAIN, optional STARTTLS).</summary>
public sealed class SmtpHostedService : BackgroundService
{
    private const int BytesPerMegabyte = 1024 * 1024;

    private readonly SmtpOptions _options;
    private readonly RelayConfigCache _cache;
    private readonly RelayUserAuthenticator _authenticator;
    private readonly RelayMailboxFilter _mailboxFilter;
    private readonly RelayMessageStore _messageStore;
    private readonly ILogger<SmtpHostedService> _logger;

    public SmtpHostedService(IOptions<SmtpOptions> options, RelayConfigCache cache, RelayUserAuthenticator authenticator,
        RelayMailboxFilter mailboxFilter, RelayMessageStore messageStore, ILogger<SmtpHostedService> logger)
    {
        _options = options.Value;
        _cache = cache;
        _authenticator = authenticator;
        _mailboxFilter = mailboxFilter;
        _messageStore = messageStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ohne geladene Benutzer wäre jede Anmeldung ungültig - daher erst nach dem ersten Laden lauschen.
        await _cache.WaitForInitialLoadAsync(stoppingToken);

        var certificate = CertificateLoader.FindInMachineStore(_options.CertificateThumbprint);
        bool allowAuthWithoutTls = certificate is null || !_options.RequireTls;

        var serverOptions = new SmtpServerOptionsBuilder()
            .ServerName(_options.ServerName)
            .MaxMessageSize(_options.MaxMessageSizeMb * BytesPerMegabyte, MaxMessageSizeHandling.Strict)
            .Endpoint(builder =>
            {
                builder.Port(_options.Port, false)
                       .AuthenticationRequired(true)
                       .AllowUnsecureAuthentication(allowAuthWithoutTls);
                if (certificate is not null)
                {
                    builder.Certificate(certificate);
                }
            })
            .Build();

        var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
        serviceProvider.Add(_authenticator);
        serviceProvider.Add(_mailboxFilter);
        serviceProvider.Add(_messageStore);

        var server = new SmtpServer.SmtpServer(serverOptions, serviceProvider);
        _logger.LogInformation("SMTP-Eingang lauscht auf Port {Port} (STARTTLS: {Tls}).", _options.Port, certificate is not null);

        try
        {
            await server.StartAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogCritical(ex, "SMTP-Eingang konnte nicht gestartet werden (Port {Port} belegt?).", _options.Port);
            throw;
        }
    }
}
