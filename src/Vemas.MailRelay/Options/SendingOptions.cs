namespace Vemas.MailRelay.Options;

/// <summary>Einstellungen des Versands (Abschnitt "Sending").</summary>
public sealed class SendingOptions
{
    public const string SectionName = "Sending";

    private static readonly int[] DefaultRetryDelays = [1, 5, 15, 60, 240];

    /// <summary>
    /// Wartezeiten in Minuten vor jeder Wiederholung. Der erste Versuch erfolgt sofort,
    /// danach eine Wiederholung je Eintrag. Ohne Angabe: 1, 5, 15, 60, 240.
    /// (Kein Initialwert, da der Config-Binder Arrays sonst ergänzt statt ersetzt.)
    /// </summary>
    public int[] RetryDelaysMinutes { get; set; }

    /// <summary>Anzahl parallel versendeter Mails.</summary>
    public int MaxParallel { get; set; } = 4;

    /// <summary>Abfrageintervall der Queue in Sekunden (neue Mails werden zusätzlich sofort signalisiert).</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    public IReadOnlyList<int> GetRetryDelays()
    {
        return RetryDelaysMinutes is { Length: > 0 } ? RetryDelaysMinutes : DefaultRetryDelays;
    }
}
