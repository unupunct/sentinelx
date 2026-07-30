namespace SentinelX.Infrastructure.EventLog;

public enum EventEntryLevel
{
    Information,
    Warning,
    Error,
    Critical
}

public sealed class EventEntry
{
    public DateTime TimeCreated { get; init; }
    public string LogName { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public int Id { get; init; }
    public EventEntryLevel Level { get; init; }
    public string Message { get; init; } = "";

    /// <summary>
    /// Plain-English explanation for a small set of very common, well-documented Windows
    /// event IDs (see <see cref="EventKnowledgeBase"/>) — null for anything not in that seed
    /// list. This is diagnose-only: it never suggests anything beyond what the user can do
    /// themselves, and Sentinel X never modifies services/drivers/settings on the user's behalf.
    /// </summary>
    public string? KnownExplanation { get; init; }
    public string? RecommendedAction { get; init; }
}
