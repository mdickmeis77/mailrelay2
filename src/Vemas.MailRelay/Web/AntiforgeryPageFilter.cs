using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Vemas.MailRelay.Web;

/// <summary>
/// Prüft den Formular-Fälschungsschutz bei POST selbst (statt der automatischen Prüfung), damit bei einer
/// Ablehnung der konkrete Grund angezeigt und mit Benutzer und Browser in der Ereignisanzeige protokolliert wird.
/// </summary>
public sealed class AntiforgeryPageFilter : IAsyncPageFilter
{
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<AntiforgeryPageFilter> _logger;

    public AntiforgeryPageFilter(IAntiforgery antiforgery, ILogger<AntiforgeryPageFilter> logger)
    {
        _antiforgery = antiforgery;
        _logger = logger;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        return Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method))
        {
            await next();
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            var diagnosis = AntiforgeryDiagnosis.Create(context.HttpContext, ex);
            _logger.LogWarning(ex,
                "Formular abgelehnt (Fälschungsschutz): {Reason} | Benutzer: {User} | Seite: {Path} | Cookie vorhanden: {HasCookie} | Feld vorhanden: {HasField} | Länge: {Length} | Browser: {Agent}",
                diagnosis.Reason, diagnosis.UserName, request.Path, diagnosis.HasCookie, diagnosis.HasFormField,
                request.ContentLength, request.Headers.UserAgent.ToString());

            var tempDataFactory = context.HttpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
            context.Result = new ViewResult
            {
                ViewName = "~/Pages/Shared/FormRejected.cshtml",
                StatusCode = StatusCodes.Status400BadRequest,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState) { Model = diagnosis },
                TempData = tempDataFactory.GetTempData(context.HttpContext)
            };
            return;
        }

        await next();
    }
}
