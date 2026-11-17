using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Net.Mail;
using Vemas.MailRelay.Configuration;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Security;
using Vemas.MailRelay.Sending;
using Vemas.MailRelay.Web;

namespace Vemas.MailRelay.Pages.Profiles;

/// <summary>Profil anlegen/bearbeiten, Secret setzen und Testmail senden.</summary>
public sealed class EditModel : PageModel
{
    private const string WildcardPrefix = "*@";

    private readonly ProfileRepository _profileRepository;
    private readonly CustomerRepository _customerRepository;
    private readonly RelayConfigCache _cache;
    private readonly TestMailService _testMailService;

    public EditModel(ProfileRepository profileRepository, CustomerRepository customerRepository, RelayConfigCache cache, TestMailService testMailService)
    {
        _profileRepository = profileRepository;
        _customerRepository = customerRepository;
        _cache = cache;
        _testMailService = testMailService;
    }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public bool HasSecret { get; private set; }

    public bool IsNew => Input.ProfileId == 0;

    public List<Domain.Customer> Customers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Customers = await _customerRepository.GetAllAsync(HttpContext.RequestAborted);
        if (id is null)
        {
            return Page();
        }

        var profile = await _profileRepository.GetAsync(id.Value, HttpContext.RequestAborted);
        if (profile is null)
        {
            return NotFound();
        }

        Input = ProfileInput.FromProfile(profile);
        HasSecret = profile.HasSecret;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        Customers = await _customerRepository.GetAllAsync(cancellationToken);
        var existing = IsNew ? null : await _profileRepository.GetAsync(Input.ProfileId, cancellationToken);
        HasSecret = existing?.HasSecret ?? false;

        ValidateInput();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        byte[] newSecret = string.IsNullOrWhiteSpace(Input.NewSecret) ? null : SecretProtector.Protect(Input.NewSecret.Trim());
        try
        {
            int id = await _profileRepository.SaveAsync(Input.ToProfile(), newSecret, cancellationToken);
            await _cache.RefreshAsync(cancellationToken);
            TempData["Message"] = $"Profil '{Input.Name}' gespeichert.";
            return RedirectToPage(new { id });
        }
        catch (SqlException ex) when (SqlErrors.IsDuplicateKey(ex))
        {
            ModelState.AddModelError("Input.Name", "Ein Profil mit diesem Namen existiert bereits.");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostTestMailAsync(int id, string testRecipient)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var profile = await _profileRepository.GetAsync(id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (!MailAddress.TryCreate(testRecipient?.Trim(), out var recipient))
        {
            TempData["Error"] = "Bitte eine gültige Empfängeradresse angeben.";
            return RedirectToPage(new { id });
        }

        long messageId = await _testMailService.EnqueueAsync(profile, recipient.Address, User.Identity?.Name, cancellationToken);
        TempData["Message"] = "Testmail wurde in die Queue gestellt. Ergebnis siehe Verlauf.";
        return RedirectToPage("/Messages/Details", new { id = messageId });
    }

    private void ValidateInput()
    {
        if (!HasSecret && string.IsNullOrWhiteSpace(Input.NewSecret))
        {
            ModelState.AddModelError("Input.NewSecret", "Bitte ein Client Secret eintragen.");
        }

        if (!Guid.TryParse(Input.ClientId, out _))
        {
            ModelState.AddModelError("Input.ClientId", "Die Client-ID muss eine GUID sein.");
        }

        foreach (string pattern in Input.GetAllowedSenderList())
        {
            string candidate = pattern.StartsWith(WildcardPrefix, StringComparison.Ordinal) ? "x" + pattern[1..] : pattern;
            if (!MailAddress.TryCreate(candidate, out _))
            {
                ModelState.AddModelError("Input.AllowedSenders", $"Ungültiger Eintrag: {pattern}");
            }
        }
    }
}
