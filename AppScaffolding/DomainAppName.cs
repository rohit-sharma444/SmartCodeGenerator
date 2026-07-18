using System.Text;

namespace AppScaffolding;

/// <summary>Derives a PascalCase .NET project name from free-text.</summary>
public static class DomainAppName
{
    // Imperative verbs and request framing that wrap a name but are not part of it.
    // "develop Cash Book application" names an app called "Cash Book"; "develop" is the
    // command, not the name. Only unambiguous command/request words go here — words that
    // double as plausible name parts ("design", "code") are deliberately left out, so a
    // "Design Studio" is never truncated to "Studio".
    private static readonly HashSet<string> LeadingFiller = new(StringComparer.OrdinalIgnoreCase)
    {
        "develop", "build", "create", "generate", "make", "produce", "scaffold",
        "construct", "implement", "prototype", "please", "kindly",
        "a", "an", "the", "new", "my", "our",
        "i", "we", "want", "need", "would", "like", "to", "for", "can", "you",
    };

    // Generic "kind of thing" nouns that name the type, not the app. "Cash Book application"
    // is a Cash Book, not a "CashBookApplication".
    private static readonly HashSet<string> TrailingTypeNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "application", "applications", "app", "apps", "system", "systems",
        "software", "program", "programme", "programs", "tool", "tools",
        "solution", "solutions", "project", "projects", "platform", "suite", "module",
    };

    public static string FromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "GeneratedApp";

        var words = SplitWords(description);
        if (words.Count == 0)
            return "GeneratedApp";

        // Isolate the name: drop leading command/request words and trailing type nouns so the
        // verb the user used to ask ("develop") and the kind of thing ("application") don't
        // become part of the identifier.
        int start = 0;
        while (start < words.Count && LeadingFiller.Contains(words[start]))
            start++;

        int end = words.Count;
        while (end > start && TrailingTypeNouns.Contains(words[end - 1]))
            end--;

        // If stripping left nothing (e.g. "build application" is all filler and type noun),
        // fall back to the whole description rather than an empty name.
        if (start >= end)
        {
            start = 0;
            end = words.Count;
        }

        var sb = new StringBuilder();
        for (int i = start; i < end; i++)
            AppendPascalCased(sb, words[i]);

        if (sb.Length == 0)
            return "GeneratedApp";

        // A project/type name must not start with a digit.
        if (char.IsDigit(sb[0]))
            sb.Insert(0, "App");

        return sb.ToString();
    }

    /// <summary>Splits free-text into runs of letters/digits, discarding everything else.</summary>
    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }

    /// <summary>Appends a word with its first character upper-cased, the rest left as written.</summary>
    private static void AppendPascalCased(StringBuilder sb, string word)
    {
        sb.Append(char.ToUpperInvariant(word[0]));
        if (word.Length > 1)
            sb.Append(word, 1, word.Length - 1);
    }
}
