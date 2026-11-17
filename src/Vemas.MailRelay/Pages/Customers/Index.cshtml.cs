using Microsoft.AspNetCore.Mvc.RazorPages;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Pages.Customers;

/// <summary>Übersicht der Kunden mit Zugriff aus NextGen.</summary>
public sealed class IndexModel : PageModel
{
    private readonly CustomerRepository _customerRepository;
    private readonly ProfileRepository _profileRepository;

    public IndexModel(CustomerRepository customerRepository, ProfileRepository profileRepository)
    {
        _customerRepository = customerRepository;
        _profileRepository = profileRepository;
    }

    public List<Customer> Customers { get; private set; } = [];

    public Dictionary<int, int> ProfileCounts { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Customers = await _customerRepository.GetAllAsync(HttpContext.RequestAborted);
        var profiles = await _profileRepository.GetAllAsync(HttpContext.RequestAborted);
        ProfileCounts = profiles.Where(p => p.CustomerId.HasValue)
            .GroupBy(p => p.CustomerId.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
