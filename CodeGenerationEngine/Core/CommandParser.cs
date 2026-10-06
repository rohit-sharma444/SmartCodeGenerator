using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Normalizes natural language commands before intent detection.
/// Makes the generator more tolerant of user phrasing.
/// </summary>
public static partial class CommandParser
{
    private static readonly Dictionary<string, string> Synonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"] = "class",
            ["model"] = "class",
            ["object"] = "class",

            ["function"] = "method",
            ["operation"] = "method",

            ["field"] = "property",
            ["attribute"] = "property",

            ["manager"] = "service",

            ["endpoint"] = "api",

            ["storage"] = "repository",
            ["repo"] = "repository"
        };

    private static readonly Dictionary<string, string> CommonTypos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["clas"] = "class",
            ["classs"] = "class",
            ["clss"] = "class",

            ["methd"] = "method",
            ["mthod"] = "method",

            ["proprty"] = "property",
            ["propety"] = "property",

            ["servce"] = "service",

            ["repositry"] = "repository",
            ["repoistory"] = "repository",

            ["contoller"] = "controller",

            ["valdator"] = "validator"
        };

    private static readonly Dictionary<string, string> PhraseMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["provide me a list of"] = "list",
            ["give me a list of"] = "list",

            ["show me"] = "show",
            ["display all"] = "show",

            ["what projects are available"] = "list projects",
            ["what classes are available"] = "list classes",

            ["i want to use"] = "use",
            ["switch to"] = "use",
            ["work on"] = "use",

            ["build"] = "compile"
        };

    private static readonly HashSet<string> StopWords =
    [
        "please",
        "kindly",
        "could",
        "would",
        "can",
        "you",
        "for",
        "me",
        "the",
        "a",
        "an"
    ];

    public static string Normalize(string input) => Normalize(input, removeStopWords: true);

    /// <summary>
    /// <paramref name="removeStopWords"/> = false keeps words like "the"/"a"/"an" that
    /// several validation-rule parsers require verbatim ("must be in <c>the</c> past",
    /// "must be <c>a</c> valid date", "must be <c>the</c> same as ..."). Stripping them
    /// as English filler is fine for command prose but corrupts a rule fragment the user
    /// typed on purpose, silently making those rules unreachable from the command line
    /// while their own unit tests (which hand-build the fragment) keep passing.
    /// </summary>
    public static string Normalize(string input, bool removeStopWords)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = input.Trim();

        input = NormalizeWhitespace(input);

        input = NormalizePhrases(input);

        input = FixTypos(input);

        input = NormalizeSynonyms(input);

        input = NormalizeVerbs(input);

        if (removeStopWords)
            input = RemoveStopWords(input);

        input = NormalizePunctuation(input);

        input = NormalizeWhitespace(input);

        return input.Trim();
    }

    private static string NormalizeWhitespace(string input)
    {
        return MultipleSpacesRegex()
            .Replace(input, " ");
    }

    private static string NormalizePunctuation(string input)
    {
        return input
            .Replace(",", " ")
            .Replace(";", " ")
            .Replace(":", " ")
            .Replace(".", " ")
            .Replace("?", " ")
            .Replace("!", " ");
    }

    private static string NormalizePhrases(string input)
    {
        foreach (var phrase in PhraseMappings)
        {
            input = Regex.Replace(
                input,
                Regex.Escape(phrase.Key),
                phrase.Value,
                RegexOptions.IgnoreCase);
        }

        return input;
    }

    private static string NormalizeSynonyms(string input)
    {
        foreach (var pair in Synonyms)
        {
            // A capitalised word past the first is a name - a class called Manager, Model or
            // Field - and rewriting it wrote Service.cs, or found no class name at all.
            input = Regex.Replace(
                input,
                $@"\b{Regex.Escape(pair.Key)}\b",
                m => m.Index > 0 && char.IsUpper(m.Value[0]) ? m.Value : pair.Value,
                RegexOptions.IgnoreCase);
        }

        return input;
    }

    private static string FixTypos(string input)
    {
        foreach (var typo in CommonTypos)
        {
            input = Regex.Replace(
                input,
                $@"\b{Regex.Escape(typo.Key)}\b",
                typo.Value,
                RegexOptions.IgnoreCase);
        }

        return input;
    }

    private static string NormalizeVerbs(string input)
    {
        input = Regex.Replace(
            input,
            @"\bmake\b",
            "create",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"\bgenerate\b",
            "create",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"\bnew\b",
            "create",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"\bdisplay\b",
            "show",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"\bbuild\b",
            "compile",
            RegexOptions.IgnoreCase);

        return input;
    }

    /// <summary>
    /// The sentence without its filler words ("please", "a", "the" ...) and nothing else changed:
    /// no punctuation removed and no word rewritten. For text whose words are names the user chose,
    /// such as a property list, where Normalize would turn a property called New into Create.
    /// </summary>
    public static string RemoveFillerWords(string input) =>
        string.IsNullOrWhiteSpace(input) ? string.Empty : RemoveStopWords(input.Trim());

    private static string RemoveStopWords(string input)
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ',
            words.Where(w => !StopWords.Contains(
                    w, StringComparer.OrdinalIgnoreCase)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}