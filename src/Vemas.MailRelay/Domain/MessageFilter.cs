namespace Vemas.MailRelay.Domain;

/// <summary>Filter für die Protokoll-Übersicht.</summary>
public sealed class MessageFilter
{
    public string Status { get; set; }
    public int? CustomerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>Suchtext in Absender, Empfänger und Betreff.</summary>
    public string Search { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
