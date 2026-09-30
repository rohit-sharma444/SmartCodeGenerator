namespace DomainDiscovery.Bundled;

/// <summary>The domain a description matched, its score, and the words that earned it, as typed.</summary>
public sealed record DomainMatch(BundledDomain Domain, int Score, IReadOnlyList<string> MatchedWords);

/// <summary>The built-in domains, and which one (if any) a description is about.</summary>
public sealed class BundledDomainCatalog
{
    /// <summary>A domain's own name (weight 3), or three supporting words.</summary>
    public const int MatchThreshold = 3;
    private const int NameWeight = 3;
    private const string ResourcePrefix = "DomainDiscovery.Bundled.Domains.";

    private static readonly Lazy<BundledDomainCatalog> Embedded = new(LoadEmbedded);

    public BundledDomainCatalog(IReadOnlyList<BundledDomain> domains) => Domains = domains;

    /// <summary>The domains embedded in this assembly, in file-name order.</summary>
    public static BundledDomainCatalog Default => Embedded.Value;

    public IReadOnlyList<BundledDomain> Domains { get; }

    public DomainMatch? Match(string? description)
    {
        // Distinct stems: "books books books" is one piece of evidence, not three.
        var words = WordStem.Words(description ?? string.Empty)
            .GroupBy(w => w.Stem)
            .Select(g => g.First())
            .ToList();

        DomainMatch? best = null;
        var bestHasName = false;

        foreach (var domain in Domains)   // catalog order, so the first of equals stays
        {
            var hits = words.Where(w => domain.Terms.ContainsKey(w.Stem)).ToList();
            var score = hits.Sum(w => domain.Terms[w.Stem]);
            var hasName = hits.Any(w => domain.Terms[w.Stem] >= NameWeight);

            if (best is null || score > best.Score || (score == best.Score && hasName && !bestHasName))
            {
                best = new DomainMatch(domain, score, hits.Select(w => w.Word).ToList());
                bestHasName = hasName;
            }
        }

        return best is not null && best.Score >= MatchThreshold ? best : null;
    }

    private static BundledDomainCatalog LoadEmbedded()
    {
        var assembly = typeof(BundledDomainCatalog).Assembly;

        var domains = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return BundledDomain.Parse(reader.ReadToEnd());
            })
            .ToList();

        return new BundledDomainCatalog(domains);
    }
}
