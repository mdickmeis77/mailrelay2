namespace Vemas.MailRelay.Domain;

/// <summary>Kennzahlen für das Dashboard.</summary>
public sealed class DashboardStats
{
    public int SentToday { get; set; }
    public int FailedToday { get; set; }
    public int InQueue { get; set; }
    public int InRetry { get; set; }
    public DateTime? LastSentAt { get; set; }
}
