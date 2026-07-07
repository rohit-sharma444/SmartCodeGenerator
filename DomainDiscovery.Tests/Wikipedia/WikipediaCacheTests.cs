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

    [Fact]
    public void TryGet_UnreadableLockedFile_ReturnsFalse_DoesNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dd-cache-lock-" + Guid.NewGuid());
        try
        {
            var cache = new WikipediaCache(dir);
            cache.Put("extract:Locked", "cached body");
            var path = cache.KeyToPath("extract:Locked");

            // Hold the file open with no sharing so ReadAllText fails.
            using var exclusive = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = cache.TryGet("extract:Locked", out var value);

            Assert.False(result);
            Assert.Equal(string.Empty, value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
