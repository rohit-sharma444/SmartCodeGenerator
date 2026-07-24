using DomainDiscovery;
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaUpdateService;

/// <summary>
/// What the service reads from <c>appsettings.json</c>. Every value has a working default, so an
/// installation that never edits the file still updates the store in its usual place.
/// </summary>
public sealed class ServiceOptions
{
    public const string SectionName = "Updater";

    public string? StorePath { get; set; }

    public string? WorkingDirectory { get; set; }

    public double IntervalHours { get; set; } = 24;

    public string? StatusFilePath { get; set; }

    /// <summary>Fills anything left unset, deriving the paths from the store's own directory.</summary>
    public void ApplyDefaults()
    {
        StorePath = string.IsNullOrWhiteSpace(StorePath)
            ? DomainDiscoveryPipeline.DefaultDumpDatabasePath
            : StorePath;

        var storeDirectory = Path.GetDirectoryName(StorePath) ?? ".";

        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Path.Combine(storeDirectory, "incr")
            : WorkingDirectory;

        StatusFilePath = string.IsNullOrWhiteSpace(StatusFilePath)
            ? StoreUpdateStatusFile.BesideStore(StorePath)
            : StatusFilePath;

        if (IntervalHours <= 0)
            IntervalHours = 24;
    }
}
