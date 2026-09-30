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

    /// <summary>The longest phrase term, in words.</summary>
    private const int LongestPhrase = 3;

    public DomainMatch? Match(string? description)
    {
        // Every run of one to three adjacent words, keyed like a term: "point of sale" is a phrase
        // term and only counts when its words stand side by side. Distinct keys: "books books
        // books" is one piece of evidence, not three.
        var all = WordStem.Words(description ?? string.Empty);
        var words = Enumerable.Range(1, LongestPhrase)
            .SelectMany(n => Enumerable.Range(0, Math.Max(0, all.Count - n + 1))
                .Select(i => (
                    Word: string.Join(' ', all.Skip(i).Take(n).Select(w => w.Word)),
                    Stem: string.Join(' ', all.Skip(i).Take(n).Select(w => w.Stem)),
                    Start: i)))
            .GroupBy(w => w.Stem)
            .Select(g => g.First())
            .OrderBy(w => w.Start)
            .Select(w => (w.Word, w.Stem))
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
