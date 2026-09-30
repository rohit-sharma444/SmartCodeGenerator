using CodeGenerator.App;
using DomainDiscovery;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreReportTests
{
    private const string Missing = @"C:\nowhere\enwiki-full.db";

    [TestMethod]
    public void AMissingStoreUnderOllamaIsHealthyAndOneLine()
    {
        var (lines, healthy) = CodeGeneratorStarter.StoreReport(Missing, DiscoveryConfig.OllamaSource);

        Assert.IsTrue(healthy, "an optional store being absent is not a fault");
        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], Missing);
        StringAssert.Contains(lines[0], "built-in domains");
        Assert.DoesNotContain("zero classes", lines[0]);
    }

    [TestMethod]
    public void AMissingStorePinnedToWikipediaStillWarns()
    {
        var (lines, healthy) = CodeGeneratorStarter.StoreReport(Missing, DiscoveryConfig.WikipediaSource);

        Assert.IsFalse(healthy);
        Assert.IsTrue(lines.Any(l => l.Contains("online Wikipedia")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("dotnet run --project WikipediaDumpImporter")));
    }
}
