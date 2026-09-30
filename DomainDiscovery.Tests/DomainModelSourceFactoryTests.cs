using DomainDiscovery;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Llm;   // FakeOllamaClient, defined in Task 6's test file
using Xunit;

namespace DomainDiscovery.Tests;

internal sealed class StubSource : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
        => new(new CandidateDomainModel(), "Wikipedia", true, null);
}

/// <summary>A Wikipedia stub that, like the real one when no topic is recognised, has its own
/// message to report — so a fallback wrapping it has two things to say, not one.</summary>
internal sealed class StubSourceWithMessage : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
        => new(new CandidateDomainModel(), "Wikipedia", false, "No topic was recognised.");
}

public class DomainModelSourceFactoryTests
{
    private static IDomainModelSource Wikipedia() => new StubSource();

    private static LlmDomainModel OneClass() => new()
    {
        Classes =
        {
            new LlmClass
            {
                Name = "Customer",
                Role = "Party",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } }
            }
        }
    };

    [Fact]
    public void UsesOllamaWhenItIsConfiguredAndAvailable()
    {
        // Behaviour rather than type: the Ollama source is wrapped now, so that it can fall back
        // when a generation fails rather than only when the server is down.
        var built = 0;

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient { Available = true, Roster = OneClass(), Detail = OneClass() });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Equal(0, built);
    }

    [Fact]
    public void FallsBackToWikipediaWhenTheModelIsRunningButTheGenerationFails()
    {
        // The failure this whole change is about: the probe says Ollama is up, then the
        // generation stalls or comes back unparseable. That used to leave the stage red with no
        // classes at all, while a working second source sat unused.
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient { Available = true, Roster = null, RosterError = "it stopped early" });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.StartsWith("Wikipedia", outcome.SourceLabel);
    }

    [Fact]
    public void SaysWhyItFellBackAfterAFailedGeneration()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient { Available = true, Roster = null, RosterError = "it stopped early" });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("it stopped early", outcome.Message);
        Assert.Contains("Falling back to Wikipedia", outcome.Message);
    }

    [Fact]
    public void KeepsAShortAnswerFromTheModelRatherThanReplacingItWithWikipedia()
    {
        // A run stopped early still succeeded. Its classes came from the model the user asked
        // for; swapping them for encyclopaedia-derived ones would be a downgrade, not a rescue.
        var built = 0;

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient
            {
                Available = true,
                Roster = OneClass(),
                Detail = OneClass(),
                DetailWarning = "1 class(es) were kept"
            });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Equal("1 class(es) were kept", outcome.Message);
        Assert.Equal(0, built);
    }

    [Fact]
    public void PassesProgressStraightThroughToWhoeverIsDrawingIt()
    {
        var seen = new List<DiscoveryProgress>();

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient
            {
                Available = true,
                Roster = OneClass(),
                Detail = OneClass(),
                RosterProgress = OneClass().Classes
            });

        source.Discover("a shop", seen.Add);

        // Whatever else it reports, the class the model named reaches the caller unchanged.
        Assert.Contains(seen, p => p.Done.Contains("Customer [Party]")
                                   || p.Pending.Contains("Customer [Party]"));
    }

    [Fact]
    public void FallsBackToWikipediaWhenOllamaIsNotRunning()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        // The underlying source really is the Wikipedia stub — wrapped now, to annotate the
        // outcome, but the discovery it runs is still Wikipedia's.
        Assert.StartsWith("Wikipedia", outcome.SourceLabel);
    }

    [Fact]
    public void UsesWikipediaWhenItIsTheConfiguredSource()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        Assert.IsType<StubSource>(source);
    }

    [Fact]
    public void NeverBuildsTheWikipediaPipelineWhenOllamaAnswers()
    {
        var built = 0;

        DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient { Available = true });

        Assert.Equal(0, built);
    }

    [Fact]
    public void AnnotatesTheOutcomeWhenFallingBackBecauseOllamaIsUnavailable()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        Assert.True(
            outcome.SourceLabel.Contains("Ollama", StringComparison.OrdinalIgnoreCase)
            || (outcome.Message?.Contains("Ollama", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public void DoesNotAnnotateWhenWikipediaIsExplicitlyConfigured()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        var outcome = source.Discover("a shop");

        Assert.Equal("Wikipedia", outcome.SourceLabel);
        Assert.False(outcome.Message?.Contains("Ollama", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [Fact]
    public void ConcatenatesTheFallbackReasonWithTheInnerSourcesOwnMessage()
    {
        // Ollama down AND Wikipedia recognising no topic either: the user needs both facts, not
        // whichever one happened to overwrite the other.
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), () => new StubSourceWithMessage(), new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("Ollama", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No topic was recognised.", outcome.Message);
    }

    [Fact]
    public void NamesTheUnrecognisedSourceValueWhenFallingBack()
    {
        var config = new DiscoveryConfig { Source = "olama" };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("olama", outcome.Message);
    }

    // ----- the built-in domains in the chain ------------------------------

    private sealed class Recording : IDomainModelSource
    {
        private readonly string _label;
        private readonly bool _succeeds;
        private readonly List<string> _log;

        public Recording(string label, bool succeeds, List<string> log)
        {
            _label = label;
            _succeeds = succeeds;
            _log = log;
        }

        public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
        {
            _log.Add(_label);
            return new(new CandidateDomainModel(), _label, _succeeds, _succeeds ? null : $"{_label} found nothing.");
        }
    }

    private static FakeOllamaClient Down() => new() { Available = false };

    [Fact]
    public void WithNoStoreABundledMatchIsUsedBeforeOnlineWikipedia()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log), Down(),
            bundled: () => new Recording("Bundled", true, log), wikipediaIsLocal: false).Discover("x");

        Assert.Equal(new[] { "Bundled" }, log);
        Assert.StartsWith("Bundled", outcome.SourceLabel);
        Assert.Contains("did not answer. Falling back to the built-in domains.", outcome.Message);
    }

    [Fact]
    public void WithNoStoreAndNoBundledMatchOnlineWikipediaRunsNext()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log), Down(),
            bundled: () => new Recording("Bundled", false, log), wikipediaIsLocal: false).Discover("x");

        Assert.Equal(new[] { "Bundled", "Wikipedia" }, log);
        Assert.Contains("Bundled found nothing. Falling back to Wikipedia.", outcome.Message);
    }

    [Fact]
    public void AnInstalledStoreIsTriedBeforeTheBundledDomains()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", false, log), Down(),
            bundled: () => new Recording("Bundled", true, log), wikipediaIsLocal: true).Discover("x");

        Assert.Equal(new[] { "Wikipedia", "Bundled" }, log);
        Assert.Contains("Wikipedia found nothing. Falling back to the built-in domains.", outcome.Message);
    }

    [Fact]
    public void OllamaThatAnswersIsNeverFollowedByTheOthers()
    {
        var log = new List<string>();
        DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log),
            new FakeOllamaClient { Available = true, Roster = OneClass(), Detail = OneClass() },
            bundled: () => new Recording("Bundled", true, log)).Discover("a shop");

        Assert.Empty(log);
    }

    [Fact]
    public void PinnedToWikipediaTheBundledDomainsStillCatchAFailure()
    {
        var log = new List<string>();
        DomainModelSourceFactory.Create(new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource },
            () => new Recording("Wikipedia", false, log), Down(),
            bundled: () => new Recording("Bundled", true, log)).Discover("x");

        Assert.Equal(new[] { "Wikipedia", "Bundled" }, log);
    }
}
