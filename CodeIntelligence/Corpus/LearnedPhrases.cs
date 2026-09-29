using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Corpus;

/// <summary>
/// learned-phrases.tsv, embedded in this assembly: phrasings users typed that the CLI did not
/// understand, each paired with the intent of the command they typed next that it did.
/// </summary>
/// <remarks>
/// Reviewed by a person before it is committed, and embedded rather than copied beside the
/// binary so every build carries exactly the reviewed list - the same reason the corpus is
/// harvested from source and not from intents.json in the bin folder. GoldenSetTests guard it:
/// the score with it must not fall below the score without it.
/// </remarks>
public static class LearnedPhrases
{
    public const string ResourceName = "CodeIntelligence.Corpus.learned-phrases.tsv";

    public sealed record Result(IReadOnlyList<CorpusDocument> Documents, IReadOnlyList<string> Problems);

    /// <summary>The list this build ships.</summary>
    public static Result Shipped()
    {
        using var stream = typeof(LearnedPhrases).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return new Result([], [$"{ResourceName} is not embedded in this build"]);

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// One <c>phrase&lt;TAB&gt;IntentType</c> per line; blank lines and <c>#</c> comments skipped.
    /// </summary>
    /// <remarks>Never throws: a bad line is a problem reported, and costs that one phrase, not
    /// the command layer of the CLI it ships inside.</remarks>
    public static Result Parse(string tsv)
    {
        var documents = new List<CorpusDocument>();
        var problems = new List<string>();
        var lines = (tsv ?? string.Empty).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Trim().Length == 0)
            {
                problems.Add($"line {i + 1}: not '<phrase><TAB><intent>': '{line}'");
                continue;
            }

            var name = parts[1].Trim();
            if (!Enum.TryParse<IntentType>(name, out var intent) || intent == IntentType.Unknown
                || !Enum.IsDefined(intent))
            {
                problems.Add($"line {i + 1}: '{name}' is not an intent a phrase can mean");
                continue;
            }

            documents.Add(CorpusHarvester.Document(
                parts[0].Trim(), intent, CorpusSourceKind.Learned, "learned-phrases.tsv"));
        }

        return new Result(documents, problems);
    }
}
