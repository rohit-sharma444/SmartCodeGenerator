using DomainDiscovery;
using Xunit;

namespace DomainDiscovery.Tests;

public class WikipediaStoreLocationTests
{
    [Fact]
    public void DefaultsUnderProgramDataSoTheServiceAndTheCliAgree()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SmartCodeGenerator", "Wikipedia", "enwiki-full.db");

        Assert.Equal(expected, WikipediaStoreLocation.Resolve(new DiscoveryConfig()));
        Assert.Equal(expected, WikipediaStoreLocation.DefaultPath);
    }

    [Fact]
    public void BlankStorePathMeansTheDefault()
        => Assert.Equal(WikipediaStoreLocation.DefaultPath,
            WikipediaStoreLocation.Resolve(new DiscoveryConfig { StorePath = "  " }));

    [Fact]
    public void StorePathWinsAndExpandsEnvironmentVariables()
    {
        var config = new DiscoveryConfig { StorePath = @"%TEMP%\store.db" };

        Assert.Equal(
            Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "store.db")),
            WikipediaStoreLocation.Resolve(config));
    }

    [Fact]
    public void IsInstalledOnlyWhenTheFileExists()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.True(WikipediaStoreLocation.IsInstalled(new DiscoveryConfig { StorePath = file }));
            Assert.False(WikipediaStoreLocation.IsInstalled(new DiscoveryConfig { StorePath = file + ".missing" }));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void StorePathIsReadFromDiscoveryJson()
    {
        var json = Path.GetTempFileName();
        try
        {
            File.WriteAllText(json, "{ \"StorePath\": \"C:\\\\x\\\\s.db\" }");
            Assert.Equal(@"C:\x\s.db", DiscoveryConfig.Load(json).StorePath);
        }
        finally { File.Delete(json); }
    }

    // Review I7: the importer and the updater service each read discovery.json from their OWN
    // folder, so a StorePath set for the CLI never reached them. The shared file does.
    [Fact]
    public void ABlankStorePathFallsBackToTheSharedDiscoveryJson()
    {
        var shared = Path.GetTempFileName();
        try
        {
            File.WriteAllText(shared, """{ "StorePath": "C:\\shared\\s.db" }""");

            Assert.Equal(@"C:\shared\s.db", WikipediaStoreLocation.Resolve(new DiscoveryConfig(), shared));
            Assert.Equal(@"C:\own\s.db",
                WikipediaStoreLocation.Resolve(new DiscoveryConfig { StorePath = @"C:\own\s.db" }, shared));
            Assert.Equal(WikipediaStoreLocation.DefaultPath,
                WikipediaStoreLocation.Resolve(new DiscoveryConfig(), shared + ".missing"));
        }
        finally { File.Delete(shared); }
    }

    [Fact]
    public void TheSharedDiscoveryJsonSitsBesideTheDefaultStoreFolder()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SmartCodeGenerator", "discovery.json"),
            WikipediaStoreLocation.SharedConfigPath);
}
