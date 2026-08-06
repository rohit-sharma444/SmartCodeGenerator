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
            // Two documents with the same text but DIFFERENT intents are not
            // duplicates — they are a genuinely ambiguous phrase (e.g. "generate
            // design document" composes from both GenerateDocumentation and
            // GenerateDesignDocument). Keeping both is deliberate: at query time
            // the k-NN vote splits across the two intents, confidence falls below
            // the floor, and the resolver declines rather than guessing. Only
            // collapse exact (text, intent) repeats coming from multiple sources.
            .GroupBy(d => (Text: d.Text.ToLowerInvariant(), d.Intent))
            .Select(g => g.First())
            .OrderBy(d => d.Text, StringComparer.Ordinal)
            .ThenBy(d => d.Intent.ToString(), StringComparer.Ordinal)
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
    /// the phrasing a user would actually type, not the help syntax. Angle and square
    /// brackets are tracked with independent counters so a closer only matches its own
    /// opener kind: a stray "&gt;"/"]" with nothing of its own kind open is kept as a
    /// literal character (not silently dropped), and a mismatched pair like "&lt;a]"
    /// does not get treated as a closed, strippable span.</summary>
    internal static string StripPlaceholders(string command)
    {
        var builder = new StringBuilder();
        var angleDepth = 0;
        var bracketDepth = 0;

        foreach (var ch in command)
        {
            switch (ch)
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0) angleDepth--;
                    else if (bracketDepth == 0) builder.Append(ch);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    else if (angleDepth == 0) builder.Append(ch);
                    break;
                default:
                    if (angleDepth == 0 && bracketDepth == 0) builder.Append(ch);
                    break;
            }
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
