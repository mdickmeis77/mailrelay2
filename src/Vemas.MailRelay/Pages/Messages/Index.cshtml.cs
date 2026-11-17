using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Sending;

namespace Vemas.MailRelay.Pages.Messages;

/// <summary>Protokoll-Übersicht mit Filter und Mehrfachaktionen (Neu senden / Abbrechen).</summary>
public sealed class IndexModel : PageModel
{
    private const int PageSize = 50;

    private readonly MessageLogRepository _logRepository;
    private readonly QueueSignal _queueSignal;

    public IndexModel(MessageLogRepository logRepository, QueueSignal queueSignal)
    {
        _logRepository = logRepository;
        _queueSignal = queueSignal;
    }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; }

    /// <summary>Seitennummer (nicht "page", das ist in Razor Pages reserviert).</summary>
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public List<MessageListItem> Items { get; private set; } = [];

    public int Total { get; private set; }

    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);

    public async Task OnGetAsync()
    {
        var filter = new MessageFilter
        {
            Status = Status,
            FromDate = FromDate,
            ToDate = ToDate,
            Search = Search,
            Page = Math.Max(1, PageNumber),
            PageSize = PageSize
        };

        (Items, Total) = await _logRepository.SearchAsync(filter, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostResendAsync(long[] selected)
    {
        int done = 0;
        foreach (long id in selected)
        {
            if (await _logRepository.ResendAsync(id, User.Identity?.Name, HttpContext.RequestAborted))
            {
                done++;
            }
        }

        if (done > 0)
        {
            _queueSignal.Notify();
        }

        SetResultMessage(done, selected.Length, "erneut in die Queue gestellt");
        return RedirectToFilter();
    }

    public async Task<IActionResult> OnPostCancelAsync(long[] selected)
    {
        int done = 0;
        foreach (long id in selected)
        {
            if (await _logRepository.CancelAsync(id, User.Identity?.Name, HttpContext.RequestAborted))
            {
                done++;
            }
        }

        SetResultMessage(done, selected.Length, "abgebrochen");
        return RedirectToFilter();
    }

    public object RouteValuesForPage(int pageNumber)
    {
        return new
        {
            Status,
            FromDate = FromDate?.ToString("yyyy-MM-dd"),
            ToDate = ToDate?.ToString("yyyy-MM-dd"),
            Search,
            p = pageNumber
        };
    }

    private void SetResultMessage(int done, int total, string action)
    {
        if (total == 0)
        {
            TempData["Error"] = "Keine Mail ausgewählt.";
            return;
        }

        TempData["Message"] = $"{done} von {total} Mail(s) {action}.";
        if (done < total)
        {
            TempData["Error"] = "Nicht alle Mails waren im passenden Status (oder der MIME-Inhalt ist bereits gelöscht).";
        }
    }

    private RedirectToPageResult RedirectToFilter()
    {
        return RedirectToPage(RouteValuesForPage(PageNumber));
    }
}
