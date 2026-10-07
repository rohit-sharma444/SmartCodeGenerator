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

    // The words that end a description's subject and start what it does: "a dental practice THAT
    // schedules appointments...". Not "of" ("point of sale") and not "and" ("bed and breakfast").
    private static readonly HashSet<string> ClauseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "that", "which", "who", "whose", "where", "when", "with", "for",
    };

    // A project name long enough to read in a brand bar and short enough for a path.
    private const int MaxWords = 4;
    private const int MaxLength = 30;

    /// <summary>
    /// A name the user typed as a name (a rename): every word is kept, only the command words and
    /// the trailing kind of thing are dropped.
    /// </summary>
    public static string FromTypedName(string name) => Build(name, shorten: false);

    /// <summary>
    /// The name a description gives its app: the subject the description is about ("a dental
    /// practice that schedules appointments..." is a DentalPractice), a few words at most.
    /// </summary>
    /// <remarks>
    /// The whole sentence used to become the project - DentalPracticeThatSchedulesAppointments
    /// ForPatientsWithDentistsAndBillsTreatments, 84 characters, cut off in the Blazor brand bar
    /// and deepening every path under the app.
    /// </remarks>
    public static string FromDescription(string description) => Build(description, shorten: true);

    private static string Build(string text, bool shorten)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "GeneratedApp";

        var words = SplitWords(text);
        if (words.Count == 0)
            return "GeneratedApp";

        // Isolate the name: drop leading command/request words and trailing type nouns so the
        // verb the user used to ask ("develop") and the kind of thing ("application") don't
        // become part of the identifier.
        var (start, end) = Subject(words, 0, shorten);

        // "an app FOR a dental practice": the subject was only the kind of thing, so the name is
        // what follows the clause word.
        if (shorten && start >= end && end < words.Count)
            (start, end) = Subject(words, end + 1, shorten);

        // If stripping left nothing (e.g. "build application" is all filler and type noun),
        // fall back to the whole description rather than an empty name.
        if (start >= end)
        {
            start = 0;
            end = words.Count;
        }

        if (shorten)
        {
            while (end - start > 1
                   && (end - start > MaxWords || words.Skip(start).Take(end - start).Sum(w => w.Length) > MaxLength))
                end--;
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

    // From `from`: skip the command words, stop at the first clause word (when shortening), and
    // drop the trailing kind of thing. Returns an empty range when nothing is left; `end` is then
    // where the clause word stands, so the caller can look past it.
    private static (int Start, int End) Subject(List<string> words, int from, bool shorten)
    {
        var start = from;
        while (start < words.Count && LeadingFiller.Contains(words[start]))
            start++;

        var cut = start;
        while (cut < words.Count && !(shorten && ClauseWords.Contains(words[cut])))
            cut++;

        var end = cut;
        while (end > start && TrailingTypeNouns.Contains(words[end - 1]))
            end--;

        return end > start ? (start, end) : (cut, cut);
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
