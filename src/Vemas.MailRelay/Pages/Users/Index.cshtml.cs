using Microsoft.AspNetCore.Mvc.RazorPages;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Pages.Users;

/// <summary>Übersicht der SMTP-Benutzer.</summary>
public sealed class IndexModel : PageModel
{
    private readonly SmtpUserRepository _smtpUserRepository;
    private readonly ProfileRepository _profileRepository;

    public IndexModel(SmtpUserRepository smtpUserRepository, ProfileRepository profileRepository)
    {
        _smtpUserRepository = smtpUserRepository;
        _profileRepository = profileRepository;
    }

    public List<RelaySmtpUser> Users { get; private set; } = [];

    public Dictionary<int, string> ProfileNames { get; private set; } = [];

    public Dictionary<int, string> CustomerNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Users = await _smtpUserRepository.GetAllAsync(HttpContext.RequestAborted);
        var profiles = await _profileRepository.GetAllAsync(HttpContext.RequestAborted);
        ProfileNames = profiles.ToDictionary(p => p.ProfileId, p => p.Name);
        CustomerNames = profiles.Where(p => p.CustomerName != null).ToDictionary(p => p.ProfileId, p => p.CustomerName);
    }
}
