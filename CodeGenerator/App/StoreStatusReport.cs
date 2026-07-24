using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.App;

/// <summary>
/// Turns the store's date, the updater's published status, and the service's presence into the
/// lines the CLI prints at startup. Pure — no console, no clock, no file system — so it is tested
/// directly.
/// </summary>
public static class StoreStatusReport
{
    public static IReadOnlyList<string> Lines(
        int? storeDate, StoreUpdateStatus status, UpdaterServiceState service,
        string installHintCommand)
    {
        var lines = new List<string>
        {
            storeDate is { } date
                ? $"Wikipedia store: dump {date}."
                : "Wikipedia store: present, but it cannot say which dump it was built from.",
        };

        lines.Add(ServiceLine(service, status));

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

    private static string ServiceLine(UpdaterServiceState service, StoreUpdateStatus status)
        => service switch
        {
            UpdaterServiceState.NotInstalled => "  Updater service: NOT INSTALLED.",
            UpdaterServiceState.Stopped => "  Updater service: installed, but stopped.",
            UpdaterServiceState.Unknown => "  Updater service: cannot tell.",
            _ => $"  Updater service: running — {Activity(status)}",
        };

    private static string Activity(StoreUpdateStatus status) => status.State switch
    {
        StoreUpdateState.Applying => $"updating {status.Percent}% ({status.PendingDays} day(s) pending).",
        StoreUpdateState.Checking => "checking for new increments.",
        StoreUpdateState.Idle => "idle.",
        StoreUpdateState.Failed => "the last update failed.",
        _ => "no status published yet.",
    };
}
