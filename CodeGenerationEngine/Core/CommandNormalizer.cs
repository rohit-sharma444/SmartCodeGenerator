using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static class CommandNormalizer
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

            ["manager"] = "service"
        };

    public static string Normalize(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return string.Empty;

        foreach (var pair in Synonyms)
        {
            sentence = Regex.Replace(sentence,
                $@"\b{Regex.Escape(pair.Key)}\b",
                pair.Value, RegexOptions.IgnoreCase);
        }

        return sentence;
    }
}