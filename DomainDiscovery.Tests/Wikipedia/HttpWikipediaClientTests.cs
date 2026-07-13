using System.Net;
using System.Text;
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

    /// <summary>Answers every request with a fixed body, for tests that must exercise the
    /// uncached (network) path of a batch fetch rather than a pre-seeded cache entry.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _body;
        public CannedHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    private HttpWikipediaClient NetworkForbiddenClient()
        => new(_cacheDir, new HttpClient(new ThrowingHandler()));

    private HttpWikipediaClient ClientReturning(string body)
        => new(_cacheDir, new HttpClient(new CannedHandler(body)));

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
    public void FetchLeads_WhenABatchDropsATitle_TheSurvivingPage_IsNotHandedToTheOtherTitle()
    {
        // Regression test for the finding: a two-title chunk whose response contains only ONE
        // page (the API dropped the other title into query.invalid/interwiki, or it simply
        // never matched) must not let ResolveLeadForTitle's single-page fallback — which exists
        // for a genuinely single-title body — hand that one page's lead to BOTH titles. Before
        // the fix, "Bookkeeping" resolved to System's own lead ("a system is a group of
        // interacting elements") purely because it was the only page present, and that head
        // noun is Abstract — turning a silent lookup mistake into a silent deletion.
        var client = ClientReturning("""
            {"query":{"pages":{"1":{"title":"System","extract":"A system is a group of interacting elements."}}}}
            """);

        var leads = client.FetchLeads(new[] { "Bookkeeping", "System" });

        Assert.False(leads.ContainsKey("Bookkeeping"));
        Assert.Equal("A system is a group of interacting elements.", leads["System"]);
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
