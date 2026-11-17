using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Web;

namespace Vemas.MailRelay.Pages.Customers;

/// <summary>Kunde anlegen/bearbeiten und Windows-Logins zuordnen.</summary>
public sealed class EditModel : PageModel
{
    private readonly CustomerRepository _customerRepository;

    public EditModel(CustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;
    }

    [BindProperty]
    public CustomerInput Input { get; set; } = new();

    public bool IsNew => Input.CustomerId == 0;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }

        var customer = await _customerRepository.GetAsync(id.Value, HttpContext.RequestAborted);
        if (customer is null)
        {
            return NotFound();
        }

        Input = CustomerInput.FromCustomer(customer);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        foreach (string login in Input.GetLoginList().Where(l => l.Length > 256 || (l.Contains(' ') && !l.Contains('\\'))))
        {
            ModelState.AddModelError("Input.Logins", $"„{login}“ ist kein gültiger SQL-Server-Login.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            int id = await _customerRepository.SaveAsync(Input.ToCustomer(), HttpContext.RequestAborted);
            TempData["Message"] = $"Kunde '{Input.Name}' gespeichert.";
            return RedirectToPage(new { id });
        }
        catch (SqlException ex) when (SqlErrors.IsDuplicateKey(ex))
        {
            ModelState.AddModelError(string.Empty, "Kurzname oder ein Windows-Login ist bereits einem Kunden zugeordnet.");
            return Page();
        }
    }
}
