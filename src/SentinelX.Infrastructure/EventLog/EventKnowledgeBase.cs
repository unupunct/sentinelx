namespace SentinelX.Infrastructure.EventLog;

/// <summary>
/// A small, hand-curated seed list of very common, well-documented Windows event IDs — not an
/// exhaustive or automated root-cause engine (that's future work). Matching entries get a
/// plain-English explanation and a user-actionable suggestion; anything not in this list is
/// still shown, just without the extra hint line.
/// </summary>
public static class EventKnowledgeBase
{
    private sealed record Entry(string ProviderContains, int Id, string Explanation, string Action);

    private static readonly Entry[] Entries =
    {
        new("Kernel-Power", 41,
            "The system rebooted without a clean shutdown — usually a power loss, a hard hang, or a hardware/driver crash that took the whole box down.",
            "Check for overheating, a failing PSU, or a recent driver update; review the System log around this time for an earlier root-cause event."),
        new("EventLog", 6008,
            "Windows detected that the previous shutdown was unexpected (not a clean restart/shutdown).",
            "Correlate with a Kernel-Power 41 or BugCheck event near the same time to find the actual cause."),
        new("Microsoft-Windows-WER-SystemErrorReporting", 1001,
            "Windows Error Reporting logged a bugcheck (BSOD) report.",
            "Check the bugcheck code in the message for the failing driver/module; update or roll back that driver."),
        new("Application Error", 1000,
            "An application crashed with an unhandled exception.",
            "Note the faulting module in the message — a repeated faulting DLL points at a specific app or driver to update/reinstall."),
        new("Application Hang", 1002,
            "An application stopped responding (hung) and Windows detected it.",
            "If this recurs for the same app, check for a pending update or a conflicting plugin/extension for that app."),
        new("Service Control Manager", 7000,
            "A Windows service failed to start.",
            "Open services.msc, check the service's dependencies and logon account, and review its own log source for the real error."),
        new("Service Control Manager", 7009,
            "A Windows service timed out while starting.",
            "The service may be waiting on a slow dependency (disk, network, driver) — check Task Manager for resource pressure at boot."),
        new("Service Control Manager", 7011,
            "A Windows service timed out waiting for a transaction response from another service.",
            "Usually a symptom of another hung/overloaded service — check which dependency it's waiting on."),
        new("disk", 7,
            "The disk detected a bad block during a read/write.",
            "Run a SMART/disk health check on the affected drive — repeated occurrences suggest imminent disk failure."),
        new("disk", 11,
            "The driver detected a controller error on the disk.",
            "Check SATA/NVMe cabling and the drive's SMART health — controller errors can also indicate a failing drive or connection."),
        new(".NET Runtime", 1026,
            "A .NET application terminated due to an unhandled exception.",
            "Check the app's own log file (if any) for the full exception — this Windows event usually only has a summary."),
        new("DistributedCOM", 10016,
            "A COM/DCOM permission mismatch between an application and Windows — extremely common and usually benign/cosmetic.",
            "Generally safe to ignore unless it correlates with an actual functional problem you're troubleshooting.")
    };

    public static (string? Explanation, string? Action) Lookup(string providerName, int id)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id == id && providerName.Contains(entry.ProviderContains, StringComparison.OrdinalIgnoreCase))
            {
                return (entry.Explanation, entry.Action);
            }
        }
        return (null, null);
    }
}
