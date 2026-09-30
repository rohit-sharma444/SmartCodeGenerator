using System.Text.RegularExpressions;

namespace DomainDiscovery.Bundled;

/// <summary>
/// A deliberately small stemmer for matching a description against the built-in domains' terms.
/// Terms are stemmed the same way when a domain file loads, so a file says "invoicing" and a user
/// saying "invoice" or "invoices" still hits it. It never has to produce a real word: only equal
/// stems for the forms people type.
/// </summary>
public static class WordStem
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.Compiled);

    public static IReadOnlyList<(string Word, string Stem)> Words(string text) =>
        Letters.Matches(text ?? string.Empty).Select(m => (m.Value, Of(m.Value))).ToList();

    public static string Of(string word)
    {
        var w = (word ?? string.Empty).ToLowerInvariant();
        if (w.Length <= 3)
            return w;

        // Plurals.
        if (w.EndsWith("ies") && w.Length > 4)
            w = w[..^3] + "y";
        else if (w.EndsWith("es") && (w[..^2].EndsWith('s') || w[..^2].EndsWith('x')
                 || w[..^2].EndsWith("ch") || w[..^2].EndsWith("sh")))
            w = w[..^2];
        else if (w.EndsWith('s') && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is"))
            w = w[..^1];

        // Verb forms, then the doubled consonant they leave: shopping -> shopp -> shop.
        if (w.EndsWith("ing") && w.Length - 3 >= 3)
            w = Undouble(w[..^3]);
        else if (w.EndsWith("ed") && w.Length - 2 >= 3)
            w = Undouble(w[..^2]);

        // A final e, so hire/hiring and invoice/invoicing meet.
        if (w.EndsWith('e') && w.Length > 3)
            w = w[..^1];

        return w;
    }

    // bill and class keep their doubled letter; shopp and plann lose one.
    private static string Undouble(string w) =>
        w.Length >= 2 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z') ? w[..^1] : w;
}
