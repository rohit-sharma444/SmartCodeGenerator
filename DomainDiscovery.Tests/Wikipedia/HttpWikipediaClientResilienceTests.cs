using System.Net;
using System.Text;
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class HttpWikipediaClientResilienceTests
{
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

    private static HttpWikipediaClient ClientReturning(string body, out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "dd-http-test-" + Guid.NewGuid());
        return new HttpWikipediaClient(dir, new HttpClient(new CannedHandler(body)));
    }

    [Fact]
    public void Search_ErrorShapedJson_ReturnsEmpty_DoesNotThrow()
    {
        var client = ClientReturning("{\"error\":{\"code\":\"x\",\"info\":\"y\"}}", out var dir);
        try
        {
            IReadOnlyList<SearchHit> hits = client.Search("Book");
            Assert.Empty(hits);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FetchExtract_ErrorShapedJson_ReturnsEmpty_DoesNotThrow()
    {
        var client = ClientReturning("{\"error\":{\"code\":\"x\",\"info\":\"y\"}}", out var dir);
        try
        {
            Assert.Equal(string.Empty, client.FetchExtract("Nonexistent"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FetchLeads_ErrorShapedBody_LeavesTheWholeChunkUncached()
    {
        // MediaWiki can answer HTTP 200 with {"error":{...}} for readonly mode, invalid
        // params, or some throttling — the JSON parses fine but there is no query.pages at
        // all. Before the fix, FetchLeadsBatch could not tell that apart from a genuine
        // "missing article" answer (which DOES have a pages element) and wrote an empty
        // "missing" cache entry under lead:<title> for every title in the chunk. With no TTL,
        // that pins a transient failure as a permanent miss. The fix must leave every title in
        // this chunk both absent from the result AND absent from the cache, so a later
        // successful fetch is still attempted.
        var client = ClientReturning("{\"error\":{\"code\":\"readonly\",\"info\":\"y\"}}", out var dir);
        try
        {
            var leads = client.FetchLeads(new[] { "Bookkeeping", "System" });

            Assert.False(leads.ContainsKey("Bookkeeping"));
            Assert.False(leads.ContainsKey("System"));

            // Assert on the cache directory contents directly, per the review's test spec:
            // nothing at all should have been written for either title.
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir);
                Assert.DoesNotContain(files, f =>
                    Path.GetFileName(f).Contains("lead_Bookkeeping", StringComparison.Ordinal)
                    || Path.GetFileName(f).Contains("lead_System", StringComparison.Ordinal));
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
