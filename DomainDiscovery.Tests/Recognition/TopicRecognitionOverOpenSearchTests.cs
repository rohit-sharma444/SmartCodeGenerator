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
/// <em>titles</em> by prefix. A phrase built by joining the SEED TERMS — "Payroll
/// Application" — is not a title, so it matches nothing, and the seed terms are still
/// searched one at a time. (The word sequences of the user's own description are a
/// different thing and are searched; see <see cref="WikipediaTopicRecognizer"/>.)
/// </summary>
public class TopicRecognitionOverOpenSearchTests
{
    private static readonly string[] KnownTitles =
    {
        "Payroll", "Payroll tax", "Application software", "Application",
    };

    /// <summary>
    /// Emulates the two endpoints the recognizer now uses. <c>opensearch</c> matches titles by
    /// prefix and answers with an ARRAY; the lead lookup (<c>action=query&amp;prop=extracts</c>)
    /// answers with an OBJECT. Serving an array to both is what the earlier fixture did, and it hid
    /// a real never-throw violation in the client rather than exercising it.
    /// </summary>
    private sealed class OpenSearchHandler : HttpMessageHandler
    {
        /// <summary>Only the opensearch terms — the lead lookups are not searches.</summary>
        public List<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;

            var json = query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase)
                ? OpenSearch(query)
                : Extracts(query);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        private string OpenSearch(string query)
        {
            var raw = Regex.Match(query, @"search=([^&]*)").Groups[1].Value;
            var search = Uri.UnescapeDataString(raw);
            Queries.Add(search);

            var titles = KnownTitles
                .Where(t => t.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var urls = titles.Select(t => "https://en.wikipedia.org/wiki/" + t.Replace(' ', '_')).ToArray();

            return JsonSerializer.Serialize(
                new object[] { search, titles, titles.Select(_ => string.Empty).ToArray(), urls },
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        /// <summary>
        /// Answers ONLY the titles the request asked for, as the real action API does. Returning
        /// every known page regardless is what an earlier version did, and it was not a harmless
        /// shortcut: the client then handed a caller some other article's text under the title it
        /// had asked for, and a recognizer that reads an article to judge it drew its conclusion
        /// from the wrong one.
        /// <para>No article here is a disambiguation page, so every lead is an ordinary one.</para>
        /// </summary>
        private static string Extracts(string query)
        {
            var raw = Regex.Match(query, @"titles=([^&]*)").Groups[1].Value;

            var requested = Uri.UnescapeDataString(raw)
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => KnownTitles.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var pages = requested.Select((title, i) => new KeyValuePair<string, object>(
                (i + 1).ToString(),
                new { title, extract = $"{title} is a thing." }));

            return JsonSerializer.Serialize(new
            {
                query = new { pages = new Dictionary<string, object>(pages) }
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
