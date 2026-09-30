namespace DomainDiscovery;

/// <summary>
/// Where the optional Wikipedia store lives, for every program that reads or writes it: the CLI,
/// the web page, WikipediaDumpImporter and WikipediaUpdateService.
/// </summary>
/// <remarks>
/// It used to be a constant naming D:\SmartCodeGenerator-Repository\Dump, a folder that exists on
/// no machine but the one this was written on — and on that one it was emptied on 2026-09-24.
/// <para>
/// ProgramData, not the user's LocalAppData: the updater service runs as LocalSystem, whose
/// LocalAppData is the system profile's, so the service would keep one store up to date while the
/// CLI read another.
/// </para>
/// </remarks>
public static class WikipediaStoreLocation
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SmartCodeGenerator", "Wikipedia", "enwiki-full.db");

    /// <summary>
    /// The machine-wide <c>discovery.json</c>, read by every program for a <c>StorePath</c> its own
    /// file does not set.
    /// </summary>
    /// <remarks>
    /// Each program reads <c>discovery.json</c> from its own folder, and the CLI, the importer and
    /// the updater service live in three. A StorePath set only in the CLI's left the importer
    /// writing the default and the CLI looking elsewhere; set here, it moves the store for all three.
    /// </remarks>
    public static string SharedConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SmartCodeGenerator", "discovery.json");

    /// <summary>
    /// <c>StorePath</c> from this program's <c>discovery.json</c>, else from the shared one at
    /// <paramref name="sharedConfigPath"/> (default <see cref="SharedConfigPath"/>), else
    /// <see cref="DefaultPath"/>.
    /// </summary>
    public static string Resolve(DiscoveryConfig? config = null, string? sharedConfigPath = null)
    {
        var configured = (config ?? DiscoveryConfig.Load()).StorePath;

        if (string.IsNullOrWhiteSpace(configured))
        {
            var shared = sharedConfigPath ?? SharedConfigPath;
            configured = File.Exists(shared) ? DiscoveryConfig.Load(shared).StorePath : null;
        }

        return string.IsNullOrWhiteSpace(configured)
            ? DefaultPath
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
    }

    public static bool IsInstalled(DiscoveryConfig? config = null) => File.Exists(Resolve(config));
}
