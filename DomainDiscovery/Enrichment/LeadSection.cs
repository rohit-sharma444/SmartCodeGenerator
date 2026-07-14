namespace DomainDiscovery.Enrichment;

/// <summary>
/// The part of a Wikipedia article that comes before its first section heading — the paragraphs
/// that define the topic, rather than the ones that give its history, its hardware, or its
/// regional variations.
/// </summary>
/// <remarks>
/// This is the answer to a measured failure. Discovery on the full encyclopedia kept every concept
/// the Point of sale article mentions twice, which is how a POS domain acquired Restaurant,
/// Computer, Smartphone, Touchscreen, TabletComputer, DepartmentStore and Data as classes. Neither
/// obvious signal separates those from the real ones:
/// <list type="bullet">
/// <item>mention count does not — Restaurant is mentioned 35 times and Data 39, against Invoice's
/// 2 and Merchant's 4;</item>
/// <item>nor does reciprocal linking — Smartphone and Touchscreen both link back to Point of sale,
/// while Customer, Invoice, Inventory and Order do not.</item>
/// </list>
/// The lead does separate them, and not by coincidence: an encyclopedia's lead IS the definition
/// of its topic, so a domain is made of what its own definition names. Measured on Point of sale,
/// every one of the ten core classes is named in the lead and every one of the eight noise classes
/// first appears after it.
/// <para>
/// A heading has to be recognized from the prose, because <c>WikitextStripper</c> flattens
/// "== History ==" to a bare line of text and the store holds no markup. What is left is still
/// unmistakable: a heading is short, is not a list item, and does not end like a sentence.
/// </para>
/// </remarks>
public static class LeadSection
{
    /// <summary>
    /// Longest line still credible as a heading. Generous on purpose: mistaking a heading for
    /// prose lets a whole section's nouns in as classes, while mistaking prose for a heading only
    /// ends the lead early — and a candidate left out of the lead is offered as a suggestion, not
    /// discarded. The cheap failure is the one to prefer.
    /// </summary>
    private const int MaxHeadingLength = 80;

    public static string Of(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var lines = body.Split('\n');
        var lead = new List<string>();

        foreach (var line in lines)
        {
            if (IsHeading(line))
                break;

            lead.Add(line);
        }

        return string.Join('\n', lead);
    }

    /// <summary>
    /// A line that names a section rather than saying anything. Prose ends on punctuation — a full
    /// stop, a colon introducing a list, a closing quote — and a heading ends on the last letter of
    /// its last word ("Terminology", "History", "Software before the 1990s").
    /// </summary>
    private static bool IsHeading(string line)
    {
        var text = line.Trim();

        if (text.Length == 0 || text.Length > MaxHeadingLength)
            return false;

        // A list item is content, and its last character is as likely to be a letter as not
        // ("* Seller's logo").
        if (text.StartsWith("* ", StringComparison.Ordinal))
            return false;

        return char.IsLetterOrDigit(text[^1]);
    }
}
