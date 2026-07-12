using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class HttpWikipediaClientTests : IDisposable
{
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), "dd-cache-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FetchLinks_ParsesTitles_FromACachedApiResponse()
    {
        // The cache is read before the network, so seeding it exercises the parser offline.
        Directory.CreateDirectory(_cacheDir);
        WriteCache("links:Point of sale", """
            {"query":{"pages":{"1":{"links":[
                {"ns":0,"title":"Cash register"},{"ns":0,"title":"Receipt"}]}}}}
            """);

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Equal(new[] { "Cash register", "Receipt" }, client.FetchLinks("Point of sale"));
    }

    [Fact]
    public void FetchLead_ParsesTheExtract_FromACachedApiResponse()
    {
        Directory.CreateDirectory(_cacheDir);
        WriteCache("lead:Receipt", """
            {"query":{"pages":{"1":{"extract":"A receipt is a document."}}}}
            """);

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Equal("A receipt is a document.", client.FetchLead("Receipt"));
    }

    [Fact]
    public void FetchLinks_WhenTheResponseIsUnusable_IsEmpty()
    {
        Directory.CreateDirectory(_cacheDir);
        WriteCache("links:Nonsense", "{ not json");

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Empty(client.FetchLinks("Nonsense"));
    }

    // Mirrors WikipediaCache's own key-to-filename scheme.
    private void WriteCache(string key, string body)
    {
        var safe = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        File.WriteAllText(Path.Combine(_cacheDir, safe + ".cache"), body);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }
}
