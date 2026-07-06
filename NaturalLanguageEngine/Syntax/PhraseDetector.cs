using NaturalLanguageEngine.Abstractions.Syntax;
using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Syntax;

public sealed class PhraseDetector : IPhraseDetector
{
    public void Detect(Sentence sentence)
    {
        var tokens = sentence.Tokens;

        int index = 0;

        while (index < tokens.Count)
        {
            if (TryReadSpecialToken(tokens, ref index, out var special))
            {
                sentence.Phrases.Add(special);
                continue;
            }

            if (TryReadNounPhrase(tokens, ref index, out var phrase))
            {
                sentence.Phrases.Add(phrase);
                continue;
            }

            if (TryReadVerbPhrase(tokens, ref index, out phrase))
            {
                sentence.Phrases.Add(phrase);
                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Wraps lexically-typed tokens (URL, email, date, time, number) into a
    /// single-token noun phrase so they reach the semantic layer as entities.
    /// </summary>
    private bool TryReadSpecialToken(
        IList<TokenAnalysis> tokens,
        ref int index,
        out Phrase phrase)
    {
        var analysis = tokens[index];
        var type = analysis.Token.Type;

        if (type is Models.Enums.TokenType.Url
                 or Models.Enums.TokenType.Email
                 or Models.Enums.TokenType.Date
                 or Models.Enums.TokenType.Time
                 or Models.Enums.TokenType.Number)
        {
            phrase = new Phrase { Type = PhraseType.NounPhrase, Head = analysis };
            phrase.Tokens.Add(analysis);
            index++;
            return true;
        }

        phrase = null!;
        return false;
    }

    private bool TryReadNounPhrase(
    IList<TokenAnalysis> tokens,
    ref int index,
    out Phrase phrase)
    {
        phrase = new Phrase
        {
            Type = PhraseType.NounPhrase
        };

        int start = index;

        while (index < tokens.Count)
        {
            var pos = tokens[index].PartOfSpeech;

            if (pos == PartOfSpeech.Determiner ||
                pos == PartOfSpeech.Adjective)
            {
                phrase.Tokens.Add(tokens[index]);
                index++;
                continue;
            }

            if (pos == PartOfSpeech.Noun ||
                pos == PartOfSpeech.Pronoun)
            {
                phrase.Tokens.Add(tokens[index]);
                phrase.Head = tokens[index];
                index++;

                return true;
            }

            break;
        }

        index = start;

        return false;
    }

    private bool TryReadVerbPhrase(
    IList<TokenAnalysis> tokens,
    ref int index,
    out Phrase phrase)
    {
        phrase = new Phrase
        {
            Type = PhraseType.VerbPhrase
        };

        int start = index;

        while (index < tokens.Count)
        {
            var pos = tokens[index].PartOfSpeech;

            if (pos == PartOfSpeech.Auxiliary ||
                pos == PartOfSpeech.Modal)
            {
                phrase.Tokens.Add(tokens[index]);
                index++;
                continue;
            }

            if (pos == PartOfSpeech.Verb)
            {
                phrase.Tokens.Add(tokens[index]);
                phrase.Head = tokens[index];
                index++;

                return true;
            }

            break;
        }

        index = start;

        return false;
    }
}