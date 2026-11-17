using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vemas.MailRelay.Data;
using Vemas.MailRelay.Domain;
using Vemas.MailRelay.Sending;

namespace Vemas.MailRelay.Pages.Messages;

/// <summary>Detailansicht einer Mail mit allen Versuchen, Neu senden, Abbrechen und .eml-Download.</summary>
public sealed class DetailsModel : PageModel
{
    private const string EmlContentType = "message/rfc822";

    private readonly MessageLogRepository _logRepository;
    private readonly QueueSignal _queueSignal;

    public DetailsModel(MessageLogRepository logRepository, QueueSignal queueSignal)
    {
        _logRepository = logRepository;
        _queueSignal = queueSignal;
    }

    public MessageDetails Details { get; private set; }

    public bool CanResend => Details.Summary.HasMime
        && Details.Summary.Status is MessageStatus.Sent or MessageStatus.Failed or MessageStatus.Cancelled;

    public bool CanCancel => Details.Summary.Status is MessageStatus.Queued or MessageStatus.Retry;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        Details = await _logRepository.GetDetailsAsync(id, HttpContext.RequestAborted);
        return Details is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostResendAsync(long id)
    {
        if (await _logRepository.ResendAsync(id, User.Identity?.Name, HttpContext.RequestAborted))
        {
            _queueSignal.Notify();
            TempData["Message"] = "Mail wurde erneut in die Queue gestellt.";
        }
        else
        {
            TempData["Error"] = "Mail kann nicht neu gesendet werden (Status oder MIME-Inhalt bereits gelöscht).";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(long id)
    {
        if (await _logRepository.CancelAsync(id, User.Identity?.Name, HttpContext.RequestAborted))
        {
            TempData["Message"] = "Versand wurde abgebrochen.";
        }
        else
        {
            TempData["Error"] = "Mail kann nicht mehr abgebrochen werden.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetEmlAsync(long id)
    {
        byte[] mime = await _logRepository.GetMimeAsync(id, HttpContext.RequestAborted);
        return mime is null ? NotFound() : File(mime, EmlContentType, $"mailrelay-{id}.eml");
    }
}
