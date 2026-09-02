using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

internal sealed class FakeOllamaClient : IOllamaClient
{
    public LlmDomainModel? Answer { get; set; }
    public string? Error { get; set; }
    public bool Available { get; set; } = true;
    public string? LastDescription { get; private set; }

    public bool IsAvailable() => Available;

    public OllamaAnswer Ask(string description)
    {
        LastDescription = description;
        return Answer is not null ? OllamaAnswer.Success(Answer) : OllamaAnswer.Failure(Error ?? "no answer");
    }
}

public class OllamaDomainModelSourceTests
{
    private static LlmDomainModel OneClass() => new()
    {
        Classes =
        {
            new LlmClass
            {
                Name = "Customer",
                Role = "Party",
                Purpose = "someone who buys",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } }
            }
        }
    };

    [Fact]
    public void SucceedsAndLabelsTheModelThatAnswered()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        var outcome = new OllamaDomainModelSource(client, "qwen2.5:3b").Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Customer");
    }

    [Fact]
    public void FailsWithAMessageWhenTheModelAnswersNothing()
    {
        var outcome = new OllamaDomainModelSource(new FakeOllamaClient { Answer = null }, "qwen2.5:3b")
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Message);
        Assert.Empty(outcome.Model.Entities);
    }

    [Fact]
    public void ThePublishedMessageCarriesTheClientsActualReason()
    {
        // Before OllamaAnswer, every one of these collapsed into the same unhelpful sentence -
        // this is what stops that: the specific reason the client determined must survive all the
        // way to the string the user reads.
        var outcome = new OllamaDomainModelSource(
                new FakeOllamaClient { Answer = null, Error = "the server returned 500 Internal Server Error: {\"error\":\"boom\"}" },
                "qwen2.5:3b")
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.Contains("the server returned 500", outcome.Message);
        Assert.Contains("boom", outcome.Message);
    }

    [Fact]
    public void FailsWhenTheModelReturnsNoClassesAtAll()
    {
        var outcome = new OllamaDomainModelSource(
            new FakeOllamaClient { Answer = new LlmDomainModel() }, "qwen2.5:3b").Discover("a shop");

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public void PassesTheDescriptionStraightThrough()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        new OllamaDomainModelSource(client, "qwen2.5:3b").Discover("a shop that sells bicycles");

        Assert.Equal("a shop that sells bicycles", client.LastDescription);
    }
}
