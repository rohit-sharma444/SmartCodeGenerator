namespace DomainDiscovery.Model;

/// <summary>The recognized domain: a Wikipedia article and a confidence in [0,1].</summary>
public sealed record TopicResult(string Title, string ArticleUrl, double Confidence)
{
    /// <summary>No topic could be recognized (e.g. no seed entities, or offline).</summary>
    public static readonly TopicResult None = new("(unknown)", string.Empty, 0d);

    public bool IsRecognized => !string.IsNullOrEmpty(ArticleUrl);
}
