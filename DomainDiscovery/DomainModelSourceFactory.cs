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
        // unreachable Ollama costs one probe rather than a ten-minute timeout.
        if (client.IsAvailable())
            return new OllamaDomainModelSource(client, config.Model);

        return new AnnotatedFallbackSource(
            wikipedia,
            $"{config.Model} (Ollama) was configured but did not answer. Falling back to Wikipedia.");
    }

    /// <summary>
    /// Wraps a fallback source so its outcome says a fallback happened, and why — rather than
    /// looking exactly like a user who deliberately configured Wikipedia. A customer whose Ollama
    /// service has stopped must not simply observe the tool quietly getting worse.
    /// </summary>
    private sealed class AnnotatedFallbackSource : IDomainModelSource
    {
        private readonly Func<IDomainModelSource> _inner;
        private readonly string _reason;

        public AnnotatedFallbackSource(Func<IDomainModelSource> inner, string reason)
        {
            _inner = inner;
            _reason = reason;
        }

        public DiscoveryOutcome Discover(string description)
        {
            var outcome = _inner().Discover(description);

            return outcome with
            {
                SourceLabel = $"{outcome.SourceLabel} (fallback)",
                // The inner source's own message wins when it set one (Wikipedia does, when no
                // topic was recognised) — the fallback reason must not overwrite it.
                Message = outcome.Message ?? _reason
            };
        }
    }
}
