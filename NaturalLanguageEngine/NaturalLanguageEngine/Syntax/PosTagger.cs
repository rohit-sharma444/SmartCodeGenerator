using NaturalLanguageEngine.Abstractions.Syntax;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Syntax;

public sealed class PosTagger : IPosTagger
{
    private readonly ILanguageRepository _language;

    public PosTagger(ILanguageRepository language)
    {
        _language = language;
    }

    public void Tag(IList<TokenAnalysis> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var current = tokens[i];
            var word = current.Token.Text;

            // 1. Direct knowledge lookup (highest priority)
            if (_language.TryGetWord(word, out var entry))
            {
                current.PartOfSpeech = Map(entry.PartOfSpeech);
                continue;
            }

            // 2. Heuristics
            current.PartOfSpeech = InferFromPattern(tokens, i);
        }
    }

    public IReadOnlyDictionary<Token, PartOfSpeech> Tag(IReadOnlyList<Token> tokens)
    {
        var analyses = tokens
            .Select(t => new TokenAnalysis { Token = t })
            .ToList();

        Tag(analyses);

        var result = new Dictionary<Token, PartOfSpeech>();
        foreach (var analysis in analyses)
            result[analysis.Token] = analysis.PartOfSpeech;

        return result;
    }

    private PartOfSpeech InferFromPattern(
    IList<TokenAnalysis> tokens,
    int index)
    {
        var token = tokens[index].Token.Text;

        // Numbers
        if (double.TryParse(token, out _))
            return PartOfSpeech.Numeral;

        // Capitalized words → likely noun (or proper noun)
        if (char.IsUpper(token.FirstOrDefault()))
            return PartOfSpeech.Noun;

        // Common verb endings
        if (token.EndsWith("ed") || token.EndsWith("ing"))
            return PartOfSpeech.Verb;

        // Default fallback
        return PartOfSpeech.Unknown;
    }

    private PartOfSpeech Map(string? pos)
    {
        return pos?.ToLower() switch
        {
            "noun" => PartOfSpeech.Noun,
            "verb" => PartOfSpeech.Verb,
            "adjective" => PartOfSpeech.Adjective,
            "adverb" => PartOfSpeech.Adverb,
            "pronoun" => PartOfSpeech.Pronoun,
            "preposition" => PartOfSpeech.Preposition,
            "conjunction" => PartOfSpeech.Conjunction,
            "determiner" => PartOfSpeech.Determiner,
            _ => PartOfSpeech.Unknown
        };
    }
}