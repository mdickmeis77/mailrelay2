using Microsoft.AspNetCore.Mvc.RazorPages;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Pages.Profiles;

/// <summary>Übersicht der Versandprofile.</summary>
public sealed class IndexModel : PageModel
{
    private readonly ProfileRepository _profileRepository;

    public IndexModel(ProfileRepository profileRepository)
    {
        _profileRepository = profileRepository;
    }

    public List<RelayProfile> Profiles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Profiles = await _profileRepository.GetAllAsync(HttpContext.RequestAborted);
    }
}
