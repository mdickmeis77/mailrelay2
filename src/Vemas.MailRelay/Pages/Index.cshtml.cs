using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Pages;

/// <summary>Dashboard: Kennzahlen des Tages und Warnungen zu ablaufenden Secrets.</summary>
public sealed class IndexModel : PageModel
{
    private readonly DashboardRepository _dashboardRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly MaintenanceOptions _maintenanceOptions;

    public IndexModel(DashboardRepository dashboardRepository, ProfileRepository profileRepository, IOptions<MaintenanceOptions> maintenanceOptions)
    {
        _dashboardRepository = dashboardRepository;
        _profileRepository = profileRepository;
        _maintenanceOptions = maintenanceOptions.Value;
    }

    public DashboardStats Stats { get; private set; } = new();

    public List<RelayProfile> ExpiringProfiles { get; private set; } = [];

    public List<RelayProfile> ProfilesWithoutSecret { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        Stats = await _dashboardRepository.GetDashboardAsync(cancellationToken);

        var profiles = await _profileRepository.GetAllAsync(cancellationToken);
        ExpiringProfiles = profiles.Where(p => SecretExpiry.NeedsWarning(p, _maintenanceOptions.SecretExpiryWarningDays)).ToList();
        ProfilesWithoutSecret = profiles.Where(p => p.IsActive && !p.HasSecret).ToList();
    }
}
