namespace DomainDiscovery.Model;

/// <summary>The recognized domain: a Wikipedia article and a confidence in [0,1].</summary>
public sealed record TopicResult(string Title, string ArticleUrl, double Confidence)
{
    /// <summary>No topic could be recognized (e.g. no seed entities, or offline).</summary>
    public static readonly TopicResult None = new("(unknown)", string.Empty, 0d);

    public bool IsRecognized => !string.IsNullOrEmpty(ArticleUrl);

    /// <summary>
    /// The OTHER articles the user's own words named, which the domain is also made of. Empty when
    /// the description named only the one thing.
    /// </summary>
    /// <remarks>
    /// One article is one concept, and a description is usually several: "track customers, payments
    /// and invoices" names three, and enriching from the winner alone threw two of them away. These
    /// are the articles worth reading as well — see <see cref="Recognition.WikipediaTopicRecognizer"/>
    /// for what earns a place here, and in particular why an article reached through a REDIRECT does
    /// not.
    /// </remarks>
    public IReadOnlyList<string> SupportingTitles { get; init; } = Array.Empty<string>();

    /// <summary>Every article the domain should be read from, the winner first.</summary>
    public IEnumerable<string> AllTitles => new[] { Title }.Concat(SupportingTitles);
}
