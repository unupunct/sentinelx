using System.Diagnostics.Eventing.Reader;

namespace SentinelX.Infrastructure.EventLog;

/// <summary>
/// Reads recent Warning/Error/Critical entries from the System and Application Windows Event
/// Logs (both readable without elevation, unlike the Security log). Best-effort per-log — a
/// failure reading one log (access denied, log doesn't exist) doesn't prevent reading the
/// other, and a single malformed record doesn't abort the read.
/// </summary>
public sealed class EventLogService
{
    public IReadOnlyList<EventEntry> GetRecentEntries(TimeSpan lookback, int maxPerLog = 200)
    {
        var entries = new List<EventEntry>();
        foreach (var logName in new[] { "System", "Application" })
        {
            entries.AddRange(ReadLog(logName, lookback, maxPerLog));
        }
        return entries.OrderByDescending(e => e.TimeCreated).ToList();
    }

    private static List<EventEntry> ReadLog(string logName, TimeSpan lookback, int maxPerLog)
    {
        var results = new List<EventEntry>();
        try
        {
            var startTimeUtc = DateTime.UtcNow - lookback;
            // Classic Windows Event Log levels: 1=Critical, 2=Error, 3=Warning.
            var query = "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '"
                        + startTimeUtc.ToString("o") + "']]]";
            var elQuery = new EventLogQuery(logName, PathType.LogName, query) { ReverseDirection = true };
            using var reader = new EventLogReader(elQuery);

            EventRecord? record;
            while (results.Count < maxPerLog && (record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    try
                    {
                        var providerName = record.ProviderName ?? "Unknown";
                        var (explanation, action) = EventKnowledgeBase.Lookup(providerName, record.Id);
                        results.Add(new EventEntry
                        {
                            TimeCreated = record.TimeCreated ?? DateTime.MinValue,
                            LogName = logName,
                            ProviderName = providerName,
                            Id = record.Id,
                            Level = MapLevel(record.Level),
                            Message = TryFormatMessage(record),
                            KnownExplanation = explanation,
                            RecommendedAction = action
                        });
                    }
                    catch
                    {
                        // A single malformed/undecodable record shouldn't stop reading the rest.
                    }
                }
            }
        }
        catch
        {
            // Log unavailable or access denied — return whatever was collected (possibly empty).
        }
        return results;
    }

    private static string TryFormatMessage(EventRecord record)
    {
        try
        {
            var message = record.FormatDescription();
            if (string.IsNullOrWhiteSpace(message))
            {
                return "(no description available)";
            }
            // Long descriptions (stack traces, XML payloads) are trimmed for list-view display.
            return message.Length > 400 ? message[..400] + "…" : message;
        }
        catch
        {
            return "(description unavailable — provider metadata may be missing)";
        }
    }

    private static EventEntryLevel MapLevel(byte? level) => level switch
    {
        1 => EventEntryLevel.Critical,
        2 => EventEntryLevel.Error,
        3 => EventEntryLevel.Warning,
        _ => EventEntryLevel.Information
    };
}
