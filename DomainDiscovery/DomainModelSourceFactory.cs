using DomainDiscovery.Llm;

namespace DomainDiscovery;

/// <summary>Chooses the discovery source, and falls back rather than failing.</summary>
public static class DomainModelSourceFactory
{
    /// <param name="wikipedia">
    /// A factory, not an instance: building the Wikipedia pipeline opens the dump store, and the
    /// Ollama path must not pay that cost merely to have a fallback available.
    /// </param>
    /// <param name="client">Injected by tests. Built from <paramref name="config"/> otherwise.</param>
    /// <param name="bundled">
    /// The built-in domains. Null leaves the chain exactly as it was before they existed.
    /// </param>
    /// <param name="wikipediaIsLocal">
    /// Whether <paramref name="wikipedia"/> reads an installed store rather than the online API. An
    /// installed store is tried before the built-in domains; the online API after them.
    /// </param>
    public static IDomainModelSource Create(
        DiscoveryConfig config,
        Func<IDomainModelSource> wikipedia,
        IOllamaClient? client = null,
        Func<IDomainModelSource>? bundled = null,
        bool wikipediaIsLocal = false)
    {
        // Chosen deliberately: nothing to report. The built-in domains still catch a Wikipedia
        // that recognised nothing, since being pinned to Wikipedia is a preference, not a wish
        // for an empty model.
        if (string.Equals(config.Source, DiscoveryConfig.WikipediaSource, StringComparison.OrdinalIgnoreCase))
            return bundled is null
                ? wikipedia()
                : new FallbackWhenUnsuccessfulSource(wikipedia(), bundled, BundledName);

        // Everything after Ollama, in the agreed order: an installed store, then the built-in
        // domains, then online Wikipedia. The one wikipedia factory still decides store-or-online
        // for itself; this decides only where the built-in domains go around it.
        Func<IDomainModelSource> offline;
        string offlineName;

        if (bundled is null)
        {
            offline = wikipedia;
            offlineName = WikipediaName;
        }
        else if (wikipediaIsLocal)
        {
            offline = () => new FallbackWhenUnsuccessfulSource(wikipedia(), bundled, BundledName);
            offlineName = WikipediaName;
        }
        else
        {
            offline = () => new FallbackWhenUnsuccessfulSource(bundled(), wikipedia, WikipediaName);
            offlineName = BundledName;
        }

        if (!string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase))
            return new AnnotatedFallbackSource(
                offline,
                $"Discovery source \"{config.Source}\" was not recognised (expected "
                + $"\"{DiscoveryConfig.OllamaSource}\" or \"{DiscoveryConfig.WikipediaSource}\"). "
                + $"Falling back to {offlineName}.");

        client ??= new OllamaClient(config);

        // A millisecond call against /api/tags. Asked before committing to a source, so an
        // unreachable Ollama costs one probe rather than a long timeout.
        if (client.IsAvailable())
            return new FallbackWhenUnsuccessfulSource(
                new OllamaDomainModelSource(client, config.Model, config.Phases), offline, offlineName);

        return new AnnotatedFallbackSource(
            offline,
            $"{config.Model} (Ollama) was configured but did not answer. Falling back to {offlineName}.");
    }

    private const string WikipediaName = "Wikipedia";
    private const string BundledName = "the built-in domains";

    /// <summary>
    /// Composes the outcome of a source that ran only because another one could not — so the
    /// result says a fallback happened, and why, rather than looking exactly like a user who
    /// deliberately configured Wikipedia.
    /// </summary>
    /// <remarks>
    /// Both messages are kept when the inner source has one of its own (Wikipedia does, when no
    /// topic was recognised): the fallback reason alone would hide that second cause — Ollama
    /// failing AND Wikipedia recognising no topic either — and the user needs both facts, not
    /// whichever one happened to be written last.
    /// </remarks>
    private static DiscoveryOutcome Annotate(DiscoveryOutcome outcome, string reason) => outcome with
    {
        SourceLabel = $"{outcome.SourceLabel} (fallback)",
        Message = outcome.Message is null ? reason : $"{reason} {outcome.Message}"
    };

    /// <summary>A fallback for a source that could not be reached at all.</summary>
    private sealed class AnnotatedFallbackSource : IDomainModelSource
    {
        private readonly Func<IDomainModelSource> _inner;
        private readonly string _reason;

        public AnnotatedFallbackSource(Func<IDomainModelSource> inner, string reason)
        {
            _inner = inner;
            _reason = reason;
        }

        public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
            => Annotate(_inner().Discover(description, onProgress), _reason);
    }

    /// <summary>
    /// Runs the preferred source, and — when it does not produce classes — runs the fallback
    /// instead of reporting the failure.
    /// </summary>
    /// <remarks>
    /// The probe that chose Ollama only proves the server is up and the model is pulled. It cannot
    /// know whether the generation will terminate, and a local 3B model on a CPU has several ways
    /// to end a run with nothing: an unparseable answer, a stall, the whole-call budget. Every one
    /// of those used to leave the Discover stage red with a message and no classes, when a
    /// perfectly good second source was sitting right there unused.
    /// <para>
    /// Only a genuinely empty result falls through. A run that was stopped early but kept classes
    /// succeeds, and is not replaced — its classes came from the model the user asked for, and
    /// discarding them to re-derive worse ones from an encyclopaedia would be a downgrade.
    /// </para>
    /// </remarks>
    private sealed class FallbackWhenUnsuccessfulSource : IDomainModelSource
    {
        private readonly IDomainModelSource _preferred;
        private readonly Func<IDomainModelSource> _fallback;
        private readonly string _fallbackName;

        public FallbackWhenUnsuccessfulSource(
            IDomainModelSource preferred, Func<IDomainModelSource> fallback, string fallbackName)
        {
            _preferred = preferred;
            _fallback = fallback;
            _fallbackName = fallbackName;
        }

        public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
        {
            var outcome = _preferred.Discover(description, onProgress);

            if (outcome.Succeeded)
                return outcome;

            // The reason the preferred source gave is kept in full. It is the only record of what
            // actually went wrong, and without it the fallback looks like an unexplained change of
            // source.
            return Annotate(
                _fallback().Discover(description, onProgress),
                $"{outcome.Message} Falling back to {_fallbackName}.");
        }
    }
}
