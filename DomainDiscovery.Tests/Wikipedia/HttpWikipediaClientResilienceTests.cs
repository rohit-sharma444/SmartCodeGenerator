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
            IReadOnlyList<SearchHit> hits = client.Search(new[] { "Book" });
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
}
