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
        if (!string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase))
            return wikipedia();

        client ??= new OllamaClient(config);

        // A millisecond call against /api/tags. Asked before committing to a source, so an
        // unreachable Ollama costs one probe rather than a ten-minute timeout.
        return client.IsAvailable()
            ? new OllamaDomainModelSource(client, config.Model)
            : wikipedia();
    }
}
