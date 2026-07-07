using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class WikipediaCacheTests
{
    [Fact]
    public void PutThenTryGet_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dd-cache-test-" + Guid.NewGuid());
        try
        {
            var cache = new WikipediaCache(dir);

            Assert.False(cache.TryGet("search:book member", out _));

            cache.Put("search:book member", "hello");

            Assert.True(cache.TryGet("search:book member", out var value));
            Assert.Equal("hello", value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeyToPath_SanitizesInvalidCharacters()
    {
        var cache = new WikipediaCache(Path.GetTempPath());

        var path = cache.KeyToPath("extract:Library/Science?x");

        Assert.DoesNotContain('/', Path.GetFileName(path));
        Assert.DoesNotContain('?', Path.GetFileName(path));
    }
}
