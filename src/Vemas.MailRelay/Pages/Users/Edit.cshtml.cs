using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Security;
using Vemas.MailRelay.Web;

namespace Vemas.MailRelay.Pages.Users;

/// <summary>SMTP-Benutzer anlegen/bearbeiten, Passwort setzen.</summary>
public sealed class EditModel : PageModel
{
    private readonly SmtpUserRepository _smtpUserRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly RelayConfigCache _cache;

    public EditModel(SmtpUserRepository smtpUserRepository, ProfileRepository profileRepository, RelayConfigCache cache)
    {
        _smtpUserRepository = smtpUserRepository;
        _profileRepository = profileRepository;
        _cache = cache;
    }

    [BindProperty]
    public SmtpUserInput Input { get; set; } = new();

    public bool HasPassword { get; private set; }

    public bool IsNew => Input.SmtpUserId == 0;

    public List<RelayProfile> Profiles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Profiles = await _profileRepository.GetAllAsync(HttpContext.RequestAborted);
        if (id is null)
        {
            return Page();
        }

        var user = await _smtpUserRepository.GetAsync(id.Value, HttpContext.RequestAborted);
        if (user is null)
        {
            return NotFound();
        }

        Input = SmtpUserInput.FromUser(user);
        HasPassword = user.HasPassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        Profiles = await _profileRepository.GetAllAsync(cancellationToken);
        var existing = IsNew ? null : await _smtpUserRepository.GetAsync(Input.SmtpUserId, cancellationToken);
        HasPassword = existing?.HasPassword ?? false;

        if (!HasPassword && string.IsNullOrEmpty(Input.NewPassword))
        {
            ModelState.AddModelError("Input.NewPassword", "Bitte ein Passwort vergeben.");
        }

        foreach (string invalid in IpAllowList.GetInvalidEntries(Input.AllowedIps))
        {
            ModelState.AddModelError("Input.AllowedIps", $"Ungültiger Eintrag: {invalid} (erwartet z.B. 10.0.0.5 oder 10.0.1.0/24)");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        string newHash = string.IsNullOrEmpty(Input.NewPassword) ? null : PasswordHasher.Hash(Input.NewPassword);
        try
        {
            int id = await _smtpUserRepository.SaveAsync(Input.ToUser(), newHash, cancellationToken);
            await _cache.RefreshAsync(cancellationToken);
            TempData["Message"] = $"SMTP-Benutzer '{Input.Login}' gespeichert.";
            return RedirectToPage(new { id });
        }
        catch (SqlException ex) when (SqlErrors.IsDuplicateKey(ex))
        {
            ModelState.AddModelError("Input.Login", "Dieser Login existiert bereits.");
            return Page();
        }
    }
}
