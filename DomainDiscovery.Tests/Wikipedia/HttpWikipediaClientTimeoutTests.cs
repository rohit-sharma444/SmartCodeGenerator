using System.Diagnostics;
using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class HttpWikipediaClientTimeoutTests
{
    /// <summary>Never responds, so only a timeout can end the request.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    private static string NewCacheDir()
        => Path.Combine(Path.GetTempPath(), "dd-timeout-test-" + Guid.NewGuid());

    [Fact]
    public void DefaultTimeout_IsShortEnoughToNotStrandTheCli()
    {
        // HttpClient's own default is 100s, and Discover issues several requests
        // back to back, so an unset timeout can freeze `build app:` for minutes.
        Assert.True(HttpWikipediaClient.DefaultTimeout <= TimeSpan.FromSeconds(30));
        Assert.True(HttpWikipediaClient.DefaultTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void Search_RequestNeverCompletes_GivesUpAtTheTimeoutAndReturnsEmpty()
    {
        var dir = NewCacheDir();
        var client = new HttpWikipediaClient(
            dir, new HttpClient(new HangingHandler()), TimeSpan.FromMilliseconds(250));

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var hits = client.Search("Book");
            stopwatch.Stop();

            Assert.Empty(hits);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Search blocked for {stopwatch.Elapsed}; the timeout was not applied.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FetchExtract_RequestNeverCompletes_GivesUpAtTheTimeoutAndReturnsEmpty()
    {
        var dir = NewCacheDir();
        var client = new HttpWikipediaClient(
            dir, new HttpClient(new HangingHandler()), TimeSpan.FromMilliseconds(250));

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var extract = client.FetchExtract("Book");
            stopwatch.Stop();

            Assert.Equal(string.Empty, extract);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"FetchExtract blocked for {stopwatch.Elapsed}; the timeout was not applied.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TimedOutRequest_IsNotCached_SoALaterRunCanSucceed()
    {
        var dir = NewCacheDir();
        var client = new HttpWikipediaClient(
            dir, new HttpClient(new HangingHandler()), TimeSpan.FromMilliseconds(250));

        try
        {
            client.Search("Book");

            // A cached empty body would pin the failure to disk forever.
            var cached = Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
            Assert.Empty(cached);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
