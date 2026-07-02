using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical;

/// <summary>
/// Splits text into sentences based on punctuation and language rules.
/// </summary>
public sealed class SentenceSplitter : ISentenceSplitter
{
    private readonly ITokenizer _tokenizer;
    private readonly ILanguageRepository _language;

    public SentenceSplitter(
        ITokenizer tokenizer,
        ILanguageRepository language)
    {
        _tokenizer = tokenizer;
        _language = language;
    }

    public IReadOnlyList<Sentence> Split(string text)
    {
        var tokens = _tokenizer.Tokenize(text);

        var sentences = new List<Sentence>();
        var current = new Sentence();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            current.Tokens.Add(new TokenAnalysis
            {
                Token = token
            });

            if (IsSentenceBoundary(tokens, i))
            {
                current.OriginalText = BuildSentenceText(current);
                sentences.Add(current);

                current = new Sentence();
            }
        }

        if (current.Tokens.Count > 0)
        {
            current.OriginalText = BuildSentenceText(current);
            sentences.Add(current);
        }

        return sentences;
    }

    private bool IsSentenceBoundary(
        IReadOnlyList<Token> tokens,
        int index)
    {
        var current = tokens[index];

        // Only punctuation can end a sentence
        if (current.Type != TokenType.Punctuation)
            return false;

        if (current.Text is not ("." or "!" or "?"))
            return false;

        return IsSafeBoundary(tokens, index);
    }

    private bool IsSafeBoundary(
        IReadOnlyList<Token> tokens,
        int index)
    {
        if (index >= tokens.Count - 1)
            return true;

        var current = tokens[index];
        var next = tokens[index + 1];

        // Rule 1: Abbreviations (Dr., Mr., etc.)
        var prevWord = GetPreviousWord(tokens, index);
        if (!string.IsNullOrEmpty(prevWord) &&
            _language.IsAbbreviation(prevWord))
        {
            return false;
        }

        // Rule 2: Decimal numbers (3.14)
        if (IsDecimalNumberContext(tokens, index))
            return false;

        // Rule 3: Next token lowercase continuation
        if (char.IsLower(next.Text.FirstOrDefault()))
            return false;

        // Rule 4: Inline URL continuation safety
        if (next.Type == TokenType.Url)
            return false;

        // Rule 5: Inline email continuation safety
        if (next.Type == TokenType.Email)
            return false;

        return true;
    }

    private bool IsDecimalNumberContext(
        IReadOnlyList<Token> tokens,
        int index)
    {
        if (index <= 0 || index >= tokens.Count - 1)
            return false;

        var prev = tokens[index - 1];
        var next = tokens[index + 1];

        return prev.Type == TokenType.Number &&
               next.Type == TokenType.Number;
    }

    private string GetPreviousWord(
        IReadOnlyList<Token> tokens,
        int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (tokens[i].Type == TokenType.Word)
                return tokens[i].Text;
        }

        return string.Empty;
    }

    private string BuildSentenceText(Sentence sentence)
    {
        return string.Join(" ",
            sentence.Tokens.Select(t => t.Token.Text));
    }
}