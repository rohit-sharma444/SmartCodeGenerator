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

        var model = client.Ask("a shop");

        Assert.NotNull(model);
        var only = Assert.Single(model!.Classes);
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
    public void ReturnsNullRatherThanThrowingWhenOllamaErrors()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("nope", HttpStatusCode.InternalServerError)));

        Assert.Null(client.Ask("a shop"));
    }

    [Fact]
    public void ReturnsNullRatherThanThrowingOnUnparseableContent()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("""{"message":{"content":"not json"}}""")));

        Assert.Null(client.Ask("a shop"));
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
}
