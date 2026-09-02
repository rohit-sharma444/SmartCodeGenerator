using System.Net;
using System.Text;
using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>Answers every request with a canned body, so the client is testable with no Ollama.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }

    public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json")
        };
    }
}

public class OllamaClientTests
{
    private const string ChatResponse = """
        {"message":{"content":"{\"classes\":[{\"name\":\"Customer\",\"role\":\"Party\",\"purpose\":\"someone who buys\",\"properties\":[{\"name\":\"Name\",\"type\":\"string\"}],\"relationships\":[]}]}"}}
        """;

    [Fact]
    public void ParsesTheModelsJsonOutOfTheChatEnvelope()
    {
        var client = new OllamaClient(new DiscoveryConfig(), new HttpClient(new StubHandler(ChatResponse)));

        var answer = client.Ask("a shop");

        Assert.True(answer.Succeeded);
        Assert.Null(answer.Error);
        var only = Assert.Single(answer.Model!.Classes);
        Assert.Equal("Customer", only.Name);
        Assert.Equal("Party", only.Role);
        Assert.Equal("someone who buys", only.Purpose);
        Assert.Equal("Name", Assert.Single(only.Properties).Name);
    }

    [Fact]
    public void AsksTheConfiguredModelAtTheConfiguredEndpoint()
    {
        var handler = new StubHandler(ChatResponse);
        var config = new DiscoveryConfig { Model = "llama3.2:3b", Endpoint = "http://localhost:11434" };

        new OllamaClient(config, new HttpClient(handler)).Ask("a shop");

        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequestUri!.ToString());
        Assert.Contains("llama3.2:3b", handler.LastRequestBody!);
        Assert.Contains("\"stream\":false", handler.LastRequestBody!);
        Assert.Contains("\"format\"", handler.LastRequestBody!);
    }

    [Fact]
    public void SendsAContextWindowAndAResponseCapWithRealHeadroom()
    {
        // Two failure modes this guards against, both measured against the running server rather
        // than assumed:
        //  - num_ctx: the shared prompt+reply window defaults to something in the 2048-4096 range,
        //    and a thorough answer for a large domain can exceed that - the reply is cut mid-stream
        //    and no longer parses as JSON.
        //  - num_predict: left unset, a rare non-terminating generation (observed directly against
        //    the real server - see OllamaClient's ContextWindow/ResponseTokenCap remarks) runs for
        //    the client's entire timeout with no signal at all. Capping it turns that into a fast,
        //    self-explaining done_reason:"length" instead.
        // Neither value is the model's full 32768 context: that was tried first and made the
        // second failure mode worse, not better, by removing the accidental ceiling that used to
        // stop a runaway generation early.
        var handler = new StubHandler(ChatResponse);

        new OllamaClient(new DiscoveryConfig(), new HttpClient(handler)).Ask("a shop");

        Assert.Contains("\"num_ctx\":8192", handler.LastRequestBody!);
        Assert.Contains("\"num_predict\":4096", handler.LastRequestBody!);
    }

    [Fact]
    public void ReturnsNoModelRatherThanThrowingWhenOllamaErrors()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(),
            new HttpClient(new StubHandler("""{"error":"model requires more system memory"}""", HttpStatusCode.InternalServerError)));

        var answer = client.Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.Null(answer.Model);
        // The status code and Ollama's own error body must both survive - this is exactly the
        // "collapsed into one unhelpful sentence" case the OllamaAnswer split exists to fix.
        Assert.Contains("500", answer.Error);
        Assert.Contains("model requires more system memory", answer.Error);
    }

    [Fact]
    public void ReturnsNoModelRatherThanThrowingOnUnparseableContent()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("""{"message":{"content":"not json"}}""")));

        var answer = client.Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.Null(answer.Model);
        Assert.Contains("could not be parsed as JSON", answer.Error);
        // How much arrived, and the tail of it - truncation is the classic cause, and this is the
        // evidence that tells you so without re-running anything.
        Assert.Contains("8 characters", answer.Error);
        Assert.Contains("not json", answer.Error);
    }

    [Fact]
    public void ReportsADoneReasonOfLengthExplicitly()
    {
        // The single most likely real-world cause of an unparseable response: Ollama stopped
        // generating because it hit the context/token limit before the model reached its own
        // stop point, and whatever content arrived is JSON truncated mid-object.
        var truncated = """{"done_reason":"length","message":{"content":"{\"classes\":[{\"name\":\"Product\""}}""";

        var client = new OllamaClient(new DiscoveryConfig(), new HttpClient(new StubHandler(truncated)));

        var answer = client.Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.Contains("done_reason", answer.Error);
        Assert.Contains("\"length\"", answer.Error);
        Assert.Contains("context limit", answer.Error);
    }

    /// <summary>Never answers — used to trip _http.Timeout rather than answering in time.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public void ReportsATimeoutByName()
    {
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };

        var answer = new OllamaClient(new DiscoveryConfig(), http).Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.Contains("timed out", answer.Error);
    }

    [Fact]
    public void ReportsAnUnreachableServerByName()
    {
        // No listener on this port - a real connection-refused, not a stub.
        var config = new DiscoveryConfig { Endpoint = "http://127.0.0.1:1" };

        var answer = new OllamaClient(config, new HttpClient()).Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.Contains("could not reach", answer.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAvailableWhenTagsAnswers()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("""{"models":[{"name":"qwen2.5:3b"}]}""")));

        Assert.True(client.IsAvailable());
    }

    [Fact]
    public void IsNotAvailableWhenTagsFails()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("", HttpStatusCode.ServiceUnavailable)));

        Assert.False(client.IsAvailable());
    }

    [Fact]
    public void IsNotAvailableWhenTheConfiguredModelIsNotInThePulledList()
    {
        // Ollama answers 200 and lists models - just not the one discovery.json names. Before the
        // fix this returned true on the status code alone, which is exactly the "Ollama is up but
        // the model was never pulled" case that sent the user to Review with zero classes.
        var client = new OllamaClient(
            new DiscoveryConfig { Model = "qwen2.5:3b" },
            new HttpClient(new StubHandler("""{"models":[{"name":"llama3.2:3b"},{"name":"mistral:7b"}]}""")));

        Assert.False(client.IsAvailable());
    }

    [Fact]
    public void IsNotAvailableWhenNoModelsArePulledAtAll()
    {
        var client = new OllamaClient(
            new DiscoveryConfig { Model = "qwen2.5:3b" },
            new HttpClient(new StubHandler("""{"models":[]}""")));

        Assert.False(client.IsAvailable());
    }

    [Fact]
    public void IsAvailableWhenTheConfiguredModelIsListedAmongOthers()
    {
        var client = new OllamaClient(
            new DiscoveryConfig { Model = "qwen2.5:3b" },
            new HttpClient(new StubHandler(
                """{"models":[{"name":"llama3.2:3b"},{"name":"qwen2.5:3b"},{"name":"mistral:7b"}]}""")));

        Assert.True(client.IsAvailable());
    }

    [Fact]
    public void AConfiguredModelWithNoTagMatchesTheLatestTag()
    {
        // A configured "qwen2.5" (no tag) means "whatever's pulled under the default tag" - Ollama
        // stores a plain "ollama pull qwen2.5" under "qwen2.5:latest".
        var client = new OllamaClient(
            new DiscoveryConfig { Model = "qwen2.5" },
            new HttpClient(new StubHandler("""{"models":[{"name":"qwen2.5:latest"}]}""")));

        Assert.True(client.IsAvailable());
    }

    [Fact]
    public void AConfiguredModelWithNoTagDoesNotMatchAnUnrelatedTag()
    {
        var client = new OllamaClient(
            new DiscoveryConfig { Model = "qwen2.5" },
            new HttpClient(new StubHandler("""{"models":[{"name":"qwen2.5:3b"}]}""")));

        Assert.False(client.IsAvailable());
    }
}
