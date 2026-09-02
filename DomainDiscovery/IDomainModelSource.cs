namespace DomainDiscovery;

/// <summary>Where the Discover stage gets a candidate class model from.</summary>
public interface IDomainModelSource
{
    /// <summary>
    /// Builds a candidate model from the user's description. Never throws: a source that cannot
    /// answer returns an outcome with <c>Succeeded</c> false and a message saying why, because
    /// the caller's job is to fall back, not to catch.
    /// </summary>
    DiscoveryOutcome Discover(string description);
}
