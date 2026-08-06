using System.Security.Cryptography;
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using NaturalLanguageBridge;

namespace CodeIntelligence.Corpus;

/// <summary>Builds the retrieval corpus from knowledge the platform already holds.
/// IntentRepository stores Actions/Targets/Keywords rather than sentences, so the
/// phrasings are composed here as action + target pairs — this is what finally puts
/// that catalog to work, since it drives none of the rule-based recognition.
/// Output is sorted and deduplicated so a reindex is reproducible.</summary>
public static class CorpusHarvester
{
    public static IReadOnlyList<CorpusDocument> Harvest()
    {
        var collected = new List<CorpusDocument>();

        collected.AddRange(FromIntentRules());
        collected.AddRange(FromLiteralTable());
        collected.AddRange(FromCommandCatalog());

        return collected
            .Where(d => d.Intent != IntentType.Unknown && d.Text.Length > 0)
            .GroupBy(d => d.Text.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(d => d.Text, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<CorpusDocument> FromIntentRules()
    {
        foreach (var rule in IntentRepository.GetAll().Where(r => r.IsEnabled))
        {
            // Actions alone are too generic to embed ("create" maps to a dozen
            // intents), so only the composed pairs and the keywords are emitted.
            foreach (var action in rule.Actions)
                foreach (var target in rule.Targets)
                    yield return Document($"{action} {target}", rule.Intent,
                        CorpusSourceKind.IntentRule, rule.Intent.ToString());

            foreach (var keyword in rule.Keywords)
                yield return Document(keyword, rule.Intent,
                    CorpusSourceKind.IntentRule, rule.Intent.ToString());
        }
    }

    private static IEnumerable<CorpusDocument> FromLiteralTable()
    {
        foreach (var intent in LiteralCommandTable.Intents)
            foreach (var phrase in LiteralCommandTable.PhrasesFor(intent))
                yield return Document(phrase, intent,
                    CorpusSourceKind.LiteralCommand, nameof(LiteralCommandTable));
    }

    private static IEnumerable<CorpusDocument> FromCommandCatalog()
    {
        foreach (var info in CommandDictionary.Commands)
        {
            var intent = CommandIntentLookup.Resolve(info.Command);
            if (intent == IntentType.Unknown)
                continue;

            yield return Document(StripPlaceholders(info.Command), intent,
                CorpusSourceKind.CommandCatalog, info.Command);

            if (!string.IsNullOrWhiteSpace(info.Example))
                yield return Document(StripPlaceholders(info.Example), intent,
                    CorpusSourceKind.CommandCatalog, info.Command);

            if (!string.IsNullOrWhiteSpace(info.Description))
                yield return Document(info.Description.TrimEnd('.'), intent,
                    CorpusSourceKind.Documentation, info.Command);
        }
    }

    /// <summary>Drops "&lt;placeholder&gt;" and "[option]" spans so the embedded text is
    /// the phrasing a user would actually type, not the help syntax.</summary>
    private static string StripPlaceholders(string command)
    {
        var builder = new StringBuilder();
        var depth = 0;

        foreach (var ch in command)
        {
            if (ch is '<' or '[') depth++;
            else if (ch is '>' or ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0) builder.Append(ch);
        }

        return string.Join(' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static CorpusDocument Document(
        string text, IntentType intent, CorpusSourceKind kind, string sourceRef)
    {
        var normalized = string.Join(' ',
            (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return new CorpusDocument(normalized, intent, kind, sourceRef, Hash(normalized, intent));
    }

    private static string Hash(string text, IntentType intent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{text}|{intent}"));
        return Convert.ToHexString(bytes);
    }
}
