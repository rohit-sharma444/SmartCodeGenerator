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

    /// <summary><c>discovery.json</c>'s <c>StorePath</c>, or <see cref="DefaultPath"/> when it is blank.</summary>
    public static string Resolve(DiscoveryConfig? config = null)
    {
        var configured = (config ?? DiscoveryConfig.Load()).StorePath;

        return string.IsNullOrWhiteSpace(configured)
            ? DefaultPath
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
    }

    public static bool IsInstalled(DiscoveryConfig? config = null) => File.Exists(Resolve(config));
}
