using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class HttpWikipediaClientTests : IDisposable
{
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), "dd-cache-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Fails the test if the client ever tries to make a real request.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                $"Unexpected network call: {request.RequestUri}");
    }

    private HttpWikipediaClient NetworkForbiddenClient()
        => new(_cacheDir, new HttpClient(new ThrowingHandler()));

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
    public void FetchLeads_ReturnsCachedLeads_WithoutAnyNetworkCall()
    {
        // Every title asked for is already on disk, so a batch fetch — which would
        // otherwise be one HTTP request per uncached title — must not touch the network.
        Directory.CreateDirectory(_cacheDir);
        WriteCache("lead:Receipt", """
            {"query":{"pages":{"1":{"extract":"A receipt is a document."}}}}
            """);
        WriteCache("lead:Barcode", """
            {"query":{"pages":{"2":{"extract":"A barcode is a machine-readable code."}}}}
            """);

        var client = NetworkForbiddenClient();

        var leads = client.FetchLeads(new[] { "Receipt", "Barcode" });

        Assert.Equal("A receipt is a document.", leads["Receipt"]);
        Assert.Equal("A barcode is a machine-readable code.", leads["Barcode"]);
    }

    [Fact]
    public void FetchLeads_WhenACachedEntryIsUnparseable_OmitsItRatherThanThrowing()
    {
        Directory.CreateDirectory(_cacheDir);
        WriteCache("lead:Nonsense", "{ not json");

        var client = NetworkForbiddenClient();

        var leads = client.FetchLeads(new[] { "Nonsense" });

        Assert.False(leads.ContainsKey("Nonsense"));
    }

    [Fact]
    public void FetchLeads_ResolvesARedirectedOrNormalizedTitle_BackToTheRequestedTitle()
    {
        // Regression test for the subtle part of batching: the API keys pages by the
        // FINAL title after normalization and redirect resolution, not by the title the
        // caller asked for. With more than one page in the response (as a real batch
        // would have), a naive "grab whatever's in pages" reader picks the wrong one —
        // only following query.normalized / query.redirects back to "POS terminal"
        // finds the right page.
        Directory.CreateDirectory(_cacheDir);
        WriteCache("lead:POS terminal", """
            {"query":{
                "normalized":[{"from":"POS terminal","to":"Pos terminal"}],
                "redirects":[{"from":"Pos terminal","to":"Payment terminal"}],
                "pages":{
                    "10":{"title":"Cash register","extract":"A cash register is a device for calculating and recording sales transactions."},
                    "11":{"title":"Payment terminal","extract":"A payment terminal is a device used at points of sale to process electronic payments."}
                }
            }}
            """);

        var client = NetworkForbiddenClient();

        var leads = client.FetchLeads(new[] { "POS terminal" });

        Assert.Equal(
            "A payment terminal is a device used at points of sale to process electronic payments.",
            leads["POS terminal"]);
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
