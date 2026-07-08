using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainDiscovery.Recognition;
using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Recognition;

/// <summary>
/// Recognizer driven through the real <see cref="HttpWikipediaClient"/> against a
/// handler that emulates MediaWiki's <c>opensearch</c> semantics: it matches article
/// <em>titles</em> by prefix. A joined phrase such as "Payroll Application" is not a
/// title, so it matches nothing — which is why each seed term must be searched alone.
/// </summary>
public class TopicRecognitionOverOpenSearchTests
{
    private static readonly string[] KnownTitles =
    {
        "Payroll", "Payroll tax", "Application software", "Application",
    };

    private sealed class OpenSearchHandler : HttpMessageHandler
    {
        public List<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var raw = Regex.Match(request.RequestUri!.Query, @"search=([^&]*)").Groups[1].Value;
            var search = Uri.UnescapeDataString(raw);
            Queries.Add(search);

            var titles = KnownTitles
                .Where(t => t.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var urls = titles.Select(t => "https://en.wikipedia.org/wiki/" + t.Replace(' ', '_')).ToArray();

            var json = JsonSerializer.Serialize(
                new object[] { search, titles, titles.Select(_ => string.Empty).ToArray(), urls },
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static void WithClient(Action<HttpWikipediaClient, OpenSearchHandler> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dd-opensearch-" + Guid.NewGuid());
        var handler = new OpenSearchHandler();
        try
        {
            body(new HttpWikipediaClient(dir, new HttpClient(handler)), handler);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Recognize_MultipleSeedTerms_RecognizesTopic_NotJoinedPhrase()
    {
        WithClient((client, handler) =>
        {
            var topic = new WikipediaTopicRecognizer(client)
                .Recognize(new[] { "Payroll", "Application" });

            Assert.True(topic.IsRecognized);
            Assert.Equal("Payroll", topic.Title);
            Assert.DoesNotContain("Payroll Application", handler.Queries);
        });
    }

    [Fact]
    public void Recognize_SearchesEachSeedTermOnItsOwn()
    {
        WithClient((client, handler) =>
        {
            new WikipediaTopicRecognizer(client).Recognize(new[] { "Payroll", "Application" });

            Assert.Equal(new[] { "Payroll", "Application" }, handler.Queries);
        });
    }

    [Fact]
    public void Recognize_NoSeedTermMatchesAnyTitle_ReturnsNone()
    {
        WithClient((client, _) =>
        {
            var topic = new WikipediaTopicRecognizer(client)
                .Recognize(new[] { "Zzyzx", "Qqqq" });

            Assert.False(topic.IsRecognized);
        });
    }
}
