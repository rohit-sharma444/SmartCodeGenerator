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
    public static IDomainModelSource Create(
        DiscoveryConfig config,
        Func<IDomainModelSource> wikipedia,
        IOllamaClient? client = null)
    {
        // Chosen deliberately: nothing to report.
        if (string.Equals(config.Source, DiscoveryConfig.WikipediaSource, StringComparison.OrdinalIgnoreCase))
            return wikipedia();

        if (!string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase))
            return new AnnotatedFallbackSource(
                wikipedia,
                $"Discovery source \"{config.Source}\" was not recognised (expected "
                + $"\"{DiscoveryConfig.OllamaSource}\" or \"{DiscoveryConfig.WikipediaSource}\"). "
                + "Falling back to Wikipedia.");

        client ??= new OllamaClient(config);

        // A millisecond call against /api/tags. Asked before committing to a source, so an
        // unreachable Ollama costs one probe rather than a long timeout.
        if (client.IsAvailable())
            return new FallbackWhenUnsuccessfulSource(
                new OllamaDomainModelSource(client, config.Model), wikipedia);

        return new AnnotatedFallbackSource(
            wikipedia,
            $"{config.Model} (Ollama) was configured but did not answer. Falling back to Wikipedia.");
    }

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

        public DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null)
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

        public FallbackWhenUnsuccessfulSource(
            IDomainModelSource preferred, Func<IDomainModelSource> fallback)
        {
            _preferred = preferred;
            _fallback = fallback;
        }

        public DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null)
        {
            var outcome = _preferred.Discover(description, onProgress);

            if (outcome.Succeeded)
                return outcome;

            // The reason the preferred source gave is kept in full. It is the only record of what
            // actually went wrong, and without it the fallback looks like an unexplained change of
            // source.
            return Annotate(
                _fallback().Discover(description, onProgress),
                $"{outcome.Message} Falling back to Wikipedia.");
        }
    }
}
