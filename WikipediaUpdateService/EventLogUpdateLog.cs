using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaUpdateService;

/// <summary>
/// Writes milestones to the Windows Event Log — the standard place to look when a service will not
/// start, and the only record that survives the status file being overwritten.
/// </summary>
/// <remarks>
/// The install script registers the source ahead of time, because creating one needs elevation and
/// the service runs long after that. It is not the only path, though: the static
/// <see cref="EventLog.WriteEntry(string, string, EventLogEntryType)"/> overload used below CREATES
/// the source itself when it is missing, and this service runs as LocalSystem, which has the
/// registry rights to do so — so a machine where the install script's registration step was skipped
/// or failed still gets a working log, just with the first write costing a little more. The
/// try/catch below still matters: registration and logging can both be refused (a locked security
/// event source table, a hardened machine), and a service that cannot log must still update the
/// store, so any failure here is swallowed rather than allowed to stop the cycle.
/// </remarks>
public sealed class EventLogUpdateLog : IUpdateLog
{
    public const string SourceName = "SmartCodeGen Wikipedia Updater";

    public void Info(string message) => Write(message, EventLogEntryType.Information);

    public void Error(string message, Exception? failure = null) => Write(
        failure is null ? message : $"{message}{Environment.NewLine}{failure}",
        EventLogEntryType.Error);

    private static void Write(string message, EventLogEntryType type)
    {
        try { EventLog.WriteEntry(SourceName, message, type); }
        catch { /* see the remarks: logging is never a reason to stop updating */ }
    }
}
