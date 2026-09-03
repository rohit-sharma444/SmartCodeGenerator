namespace DomainDiscovery;

/// <summary>Where the Discover stage gets a candidate class model from.</summary>
public interface IDomainModelSource
{
    /// <summary>
    /// Builds a candidate model from the user's description. Never throws: a source that cannot
    /// answer returns an outcome with <c>Succeeded</c> false and a message saying why, because
    /// the caller's job is to fall back, not to catch.
    /// </summary>
    /// <param name="onProgress">
    /// Called with the classes found so far, while the source is still working. Optional, and
    /// ignored by sources that have nothing to report part-way through. It is on this interface
    /// rather than on the Ollama client alone because the stage that draws the progress must not
    /// have to know which source it is waiting for.
    /// </param>
    DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null);
}
