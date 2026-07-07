namespace DomainDiscovery.Model;

/// <summary>One Wikipedia search result. <see cref="Rank"/> is 0-based; 0 = best.</summary>
public sealed record SearchHit(string Title, string Url, int Rank);
