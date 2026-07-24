using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.App;

/// <summary>
/// Turns the store's date, the updater's published status, and the service's presence into the
/// lines the CLI prints at startup. Pure — no console, no clock, no file system — so it is tested
/// directly.
/// </summary>
public static class StoreStatusReport
{
    /// <summary>
    /// An <see cref="StoreUpdateState.Applying"/> snapshot older than this is treated as stale
    /// rather than live — most likely left behind by a process that stopped without a clean finish.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    public static IReadOnlyList<string> Lines(
        int? storeDate, StoreUpdateStatus status, UpdaterServiceState service,
        string installHintCommand, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            storeDate is { } date
                ? $"Wikipedia store: dump {date}."
                : "Wikipedia store: present, but it cannot say which dump it was built from.",
        };

        lines.Add(ServiceLine(service, status, now));

        if (status.State is StoreUpdateState.Failed && !string.IsNullOrWhiteSpace(status.LastError))
            lines.Add($"  Last update error: {status.LastError}");

        if (service is UpdaterServiceState.NotInstalled or UpdaterServiceState.Stopped)
        {
            // The service is the ONLY thing that updates the store now — the CLI no longer does it.
            // So this gap is real, and the hint has to be noticed rather than skimmed past.
            lines.Add(string.Empty);
            lines.Add("  Nothing is updating the store. To install and start the updater service,");
            lines.Add("  run this from an elevated PowerShell prompt:");
            lines.Add(string.Empty);
            lines.Add($"      {installHintCommand}");
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static string ServiceLine(UpdaterServiceState service, StoreUpdateStatus status, DateTimeOffset now)
        => service switch
        {
            UpdaterServiceState.NotInstalled => "  Updater service: NOT INSTALLED.",
            UpdaterServiceState.Stopped => "  Updater service: installed, but stopped.",
            UpdaterServiceState.Unknown => "  Updater service: cannot tell.",
            _ => $"  Updater service: running — {Activity(status, now)}",
        };

    private static string Activity(StoreUpdateStatus status, DateTimeOffset now)
    {
        if (status.State is StoreUpdateState.Applying && IsStale(status, now))
        {
            // LastSuccessUtc records the last SUCCESS, not when this snapshot was written, so a
            // status left behind by a killed process reads as live otherwise — printing, say, a
            // confident "52%" for a cycle that ended hours ago.
            return "a status over an hour old, likely left by a process that stopped; treat it as unknown.";
        }

        return status.State switch
        {
            StoreUpdateState.Applying => status.CurrentDay is { } day
                ? $"applying {day} — {status.Percent}% ({status.PendingDays} day(s) pending)."
                : $"updating {status.Percent}% ({status.PendingDays} day(s) pending).",
            StoreUpdateState.Checking => "checking for new increments.",
            StoreUpdateState.Idle => "idle.",
            StoreUpdateState.Failed => "the last update failed.",

            // The exact case the service's central constraint exists for: a check that got no
            // answer must never read as a clean bill of health, or as merely "hasn't run yet".
            StoreUpdateState.Unknown =>
                "freshness could not be determined — Wikimedia could not be reached.",

            _ => "no status published yet.",
        };
    }

    private static bool IsStale(StoreUpdateStatus status, DateTimeOffset now)
        => status.UpdatedUtc is { } updated && now - updated > StaleAfter;
}
