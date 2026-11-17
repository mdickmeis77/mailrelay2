using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.EventLog;
using Vemas.MailRelay.Web;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Maintenance;
using Vemas.MailRelay.Options;
using Vemas.MailRelay.Security;
using Vemas.MailRelay.Sending;
using Vemas.MailRelay.Smtp;

const string ServiceName = "Vemas.MailRelay";

// Als Windows-Dienst ist das Arbeitsverzeichnis System32 - daher ContentRoot = EXE-Verzeichnis.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService(options => options.ServiceName = ServiceName);

// Optionen
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<WebOptions>(builder.Configuration.GetSection(WebOptions.SectionName));
builder.Services.Configure<SendingOptions>(builder.Configuration.GetSection(SendingOptions.SectionName));
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services.Configure<DatabaseMailOptions>(builder.Configuration.GetSection(DatabaseMailOptions.SectionName));

// Datenzugriff und Konfiguration
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<ProfileRepository>();
builder.Services.AddSingleton<SmtpUserRepository>();
builder.Services.AddSingleton<MessageQueueRepository>();
builder.Services.AddSingleton<MessageLogRepository>();
builder.Services.AddSingleton<MaintenanceRepository>();
builder.Services.AddSingleton<DashboardRepository>();
builder.Services.AddSingleton<DatabaseMailRepository>();
builder.Services.AddSingleton<RelayConfigCache>();

// SMTP-Eingang
builder.Services.AddSingleton<RelayUserAuthenticator>();
builder.Services.AddSingleton<RelayMailboxFilter>();
builder.Services.AddSingleton<RelayMessageStore>();

// Versand
builder.Services.AddHttpClient(GraphMailClient.GraphClientName, client =>
{
    client.BaseAddress = new Uri(GraphMailClient.GraphBaseAddress);
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient(GraphMailClient.UploadClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient(MsalHttpClientFactoryAdapter.ClientName);
builder.Services.AddSingleton<MsalHttpClientFactoryAdapter>();
builder.Services.AddSingleton<GraphTokenProvider>();
builder.Services.AddSingleton<GraphMailClient>();
builder.Services.AddSingleton<LargeMessageSender>();
builder.Services.AddSingleton<GraphMailSender>();
builder.Services.AddSingleton<DatabaseMailFeedback>();
builder.Services.AddSingleton<MessageProcessor>();
builder.Services.AddSingleton<QueueSignal>();
builder.Services.AddSingleton<TestMailService>();

// Hintergrunddienste
builder.Services.AddHostedService<ConfigRefreshService>();
builder.Services.AddHostedService<SmtpHostedService>();
builder.Services.AddHostedService<SendWorker>();
builder.Services.AddHostedService<MaintenanceService>();

// Weboberfläche: Kestrel, Windows-Anmeldung, Zugriff nur für die Admin-Gruppe
var webOptions = builder.Configuration.GetSection(WebOptions.SectionName).Get<WebOptions>() ?? new WebOptions();
var webCertificate = CertificateLoader.FindInMachineStore(webOptions.CertificateThumbprint);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Ohne Zertifikat nur lokal: Client Secrets und Passwörter sollen nicht unverschlüsselt durchs Netz gehen.
    if (webCertificate is not null)
    {
        kestrel.ListenAnyIP(webOptions.Port, listen => listen.UseHttps(webCertificate));
    }
    else if (webOptions.AllowUnencrypted)
    {
        kestrel.ListenAnyIP(webOptions.Port);
    }
    else
    {
        kestrel.ListenLocalhost(webOptions.Port);
    }
});

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(webOptions.AdminGroup)
        .Build();
});
// Formularschutz prüft AntiforgeryPageFilter selbst, um bei Ablehnung den Grund anzuzeigen und zu protokollieren.
builder.Services.AddRazorPages(options => options.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute()))
    .AddMvcOptions(options => options.Filters.Add<AntiforgeryPageFilter>());

// Schlüssel für den Formular-Fälschungsschutz fest speichern: sonst werden Formulare nach jedem
// Dienst-Neustart ungültig (400), wenn das Dienstkonto kein geladenes Benutzerprofil hat.
var keyDirectory = new DirectoryInfo(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ServiceName, "keys"));
keyDirectory.Create();
builder.Services.AddDataProtection()
    .SetApplicationName(ServiceName)
    .PersistKeysToFileSystem(keyDirectory)
    .ProtectKeysWithDpapi(protectToLocalMachine: true);

// Abgelehnte Formulare (Fälschungsschutz) mit Grund in der Ereignisanzeige protokollieren
builder.Logging.AddFilter<EventLogLoggerProvider>("Microsoft.AspNetCore.Mvc.ViewFeatures.Filters", LogLevel.Information);

var app = builder.Build();

app.UseStatusCodePages(StatusPages.WriteAsync);
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Logger.LogInformation("{Service} {Version} startet. Weboberfläche auf Port {Port}, Admin-Gruppe '{Group}'.",
    ServiceName, typeof(Program).Assembly.GetName().Version, webOptions.Port, webOptions.AdminGroup);
if (webCertificate is null && webOptions.AllowUnencrypted)
{
    app.Logger.LogWarning("Weboberfläche ohne Zertifikat von allen Rechnern per HTTP erreichbar (Web:AllowUnencrypted). " +
        "Client Secrets und Passwörter werden unverschlüsselt übertragen - Web:CertificateThumbprint setzen.");
}
else if (webCertificate is null)
{
    app.Logger.LogWarning("Kein Zertifikat für die Weboberfläche (Web:CertificateThumbprint): nur lokal auf diesem Server " +
        "erreichbar (http://localhost:{Port}/).", webOptions.Port);
}

await app.RunAsync();
