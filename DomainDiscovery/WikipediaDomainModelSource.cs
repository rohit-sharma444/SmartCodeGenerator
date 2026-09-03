namespace DomainDiscovery;

/// <summary>The original path — NLE seeds, a Wikipedia article, prose mining — behind the
/// <see cref="IDomainModelSource"/> seam. Behaviour is unchanged; only the return shape is.</summary>
public sealed class WikipediaDomainModelSource : IDomainModelSource
{
    public const string Label = "Wikipedia";

    private readonly DomainDiscoveryPipeline _pipeline;

    public WikipediaDomainModelSource(DomainDiscoveryPipeline pipeline) => _pipeline = pipeline;

    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
    {
        var result = _pipeline.Discover(description);

        return new DiscoveryOutcome(
            result.Model,
            Label,
            result.Topic.IsRecognized,
            result.Topic.IsRecognized
                ? null
                : "No topic recognized online — showing seed classes only. "
                  + "You can add classes by hand in the review.");
    }
}
