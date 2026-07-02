using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static class EnglishLanguageModel
{
    private static readonly Dictionary<string, string[]> Synonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] =
            [
                "make",
                "generate",
                "build",
                "construct",
                "develop",
                "produce"
            ],

            ["class"] =
            [
                "entity",
                "model",
                "dto"
            ],

            ["service"] =
            [
                "manager",
                "handler",
                "processor"
            ],

            ["repository"] =
            [
                "storage",
                "data access",
                "persistence"
            ],

            ["project"] =
            [
                "application",
                "solution",
                "app"
            ]
        };

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var text = input;

        foreach (var item in Synonyms)
        {
            foreach (var synonym in item.Value)
            {
                text = Regex.Replace(
                    text,
                    $@"\b{Regex.Escape(synonym)}\b",
                    item.Key,
                    RegexOptions.IgnoreCase);
            }
        }

        return text;
    }
}