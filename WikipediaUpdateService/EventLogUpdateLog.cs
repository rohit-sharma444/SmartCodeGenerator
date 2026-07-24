using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaUpdateService;

/// <summary>
/// Writes milestones to the Windows Event Log — the standard place to look when a service will not
/// start, and the only record that survives the status file being overwritten.
/// </summary>
/// <remarks>
/// The source is registered by the install script, because creating one needs elevation and the
/// service runs long after that. If the source is missing, every write here fails and is swallowed:
/// a service that cannot log must still update the store.
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
