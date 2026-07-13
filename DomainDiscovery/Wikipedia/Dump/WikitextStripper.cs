using System.Text;
using System.Text.RegularExpressions;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Reduces wikitext to prose, and collects the article's outgoing links on the way.
/// </summary>
/// <remarks>
/// This is the job the MediaWiki API did for us with <c>explaintext=1</c>, and it is the
/// quality-critical step of the whole offline path: every consumer downstream — HypernymExtractor,
/// EntityHarvester, RelationshipMiner, PropertyMiner — reads only what this class emits.
/// <para>
/// It deviates from explaintext in exactly one way, on purpose: a wikitext list item survives as a
/// <c>"* "</c> line prefix. Flat plain text throws that structure away, and an article states a
/// concept's properties precisely there ("An invoice typically contains: * invoice number * ...").
/// </para>
/// </remarks>
public static class WikitextStripper
{
    /// <summary>Link namespaces that are not article references and must not reach either output.</summary>
    private static readonly string[] NonArticlePrefixes =
        ["File:", "Image:", "Category:", "Media:", "Template:", "Help:", "Wikipedia:", "Portal:"];

    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RefTags = new(@"<ref[^>]*?/>|<ref[^>]*?>.*?</ref>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtherTags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Emphasis = new(@"'{2,5}", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*={2,6}\s*(.*?)\s*={2,6}\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^[*#:;]+\s*", RegexOptions.Compiled);
    private static readonly Regex BlankRun = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s", RegexOptions.Compiled);

    /// <summary>
    /// Prose and links for <paramref name="wikitext"/>. Never throws: unbalanced markup degrades
    /// to whatever prose can still be recovered, because one malformed article out of seven
    /// million must not stop an import that takes two hours.
    /// </summary>
    public static StrippedArticle Parse(string wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
            return new StrippedArticle(string.Empty, Array.Empty<string>());

        try
        {
            var text = Comments.Replace(wikitext, string.Empty);
            text = RefTags.Replace(text, string.Empty);
            text = RemoveNested(text, "{{", "}}");   // templates, possibly nested
            text = RemoveNested(text, "{|", "|}");   // tables, possibly nested

            var links = new List<string>();
            text = UnwrapLinks(text, links);

            text = OtherTags.Replace(text, string.Empty);
            text = Emphasis.Replace(text, string.Empty);
            text = Heading.Replace(text, "$1");

            return new StrippedArticle(NormalizeLines(text), links);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return new StrippedArticle(string.Empty, Array.Empty<string>());
        }
    }

    /// <summary>The first sentence of <paramref name="body"/>, or empty.</summary>
    public static string LeadOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var firstParagraph = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.StartsWith("* ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(firstParagraph))
            return string.Empty;

        var sentence = SentenceEnd.Split(firstParagraph).FirstOrDefault() ?? string.Empty;
        return sentence.Trim();
    }

    /// <summary>
    /// Removes every <paramref name="open"/>..<paramref name="close"/> span, counting depth so a
    /// nested template ({{Infobox|a={{nested}}}}) is removed whole rather than leaving its tail
    /// behind. A span left unclosed at end-of-input is dropped along with the rest.
    /// </summary>
    private static string RemoveNested(string text, string open, string close)
    {
        var result = new StringBuilder(text.Length);
        var depth = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, open, 0, open.Length) == 0)
            {
                depth++;
                i += open.Length;
            }
            else if (depth > 0 && string.CompareOrdinal(text, i, close, 0, close.Length) == 0)
            {
                depth--;
                i += close.Length;
            }
            else
            {
                if (depth == 0)
                    result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Replaces every [[target|display]] with its display text and [[target]] with its target,
    /// appending each mainspace target to <paramref name="links"/>. A File/Category/Image link is
    /// neither prose nor a concept, so it contributes to neither output.
    /// </summary>
    private static string UnwrapLinks(string text, List<string> links)
    {
        var result = new StringBuilder(text.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, "[[", 0, 2) != 0)
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            var end = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                // Unclosed link: keep the remaining prose rather than dropping the article's tail.
                result.Append(text, i + 2, text.Length - i - 2);
                break;
            }

            var inner = text.Substring(i + 2, end - i - 2);
            i = end + 2;

            var pipe = inner.IndexOf('|');
            var target = (pipe < 0 ? inner : inner[..pipe]).Trim();
            var display = (pipe < 0 ? inner : inner[(pipe + 1)..]).Trim();

            if (NonArticlePrefixes.Any(p => target.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (target.Length > 0 && seen.Add(target))
                links.Add(target);

            result.Append(display);
        }

        return result.ToString();
    }

    /// <summary>
    /// Trims each line, rewrites any list bullet to a single "* " marker, drops blank lines, and
    /// collapses the result. Nesting depth is deliberately flattened: PropertyMiner cares that an
    /// item IS a list item, not how deeply indented it was.
    /// </summary>
    private static string NormalizeLines(string text)
    {
        var lines = new List<string>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (ListItem.IsMatch(line))
            {
                var content = ListItem.Replace(line, string.Empty).Trim();
                if (content.Length > 0)
                    lines.Add("* " + content);
                continue;
            }

            lines.Add(line);
        }

        return BlankRun.Replace(string.Join('\n', lines), "\n\n").Trim();
    }
}
