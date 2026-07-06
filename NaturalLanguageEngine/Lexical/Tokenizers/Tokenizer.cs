using System;
using System.Collections.Generic;
using System.Linq;
using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Lexical.Tokenizers;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical;

public sealed class Tokenizer : ITokenizer
{
    private readonly ICharacterReader _reader;
    private readonly List<ITokenRecognizer> _recognizers;

    public Tokenizer(ICharacterReader reader, IEnumerable<ITokenRecognizer> recognizers)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _recognizers = (recognizers ?? Enumerable.Empty<ITokenRecognizer>())
            .OrderByDescending(r => r.Priority)
            .ToList();
    }

    public IReadOnlyList<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();

        _reader.Reset(text);

        if (!_reader.MoveNext())
            return tokens;

        while (!_reader.EndOfInput)
        {
            if (CharacterClassifier.IsWhiteSpace(_reader.Current))
            {
                _reader.MoveNext();
                continue;
            }

            bool matched = false;

            foreach (var recognizer in _recognizers)
            {
                if (!recognizer.CanRead(_reader))
                    continue;

                tokens.Add(recognizer.Read(_reader));
                matched = true;
                break;
            }

            if (!matched)
            {
                // fallback safety net
                tokens.Add(TokenFactory.Create(
                    _reader.Current.ToString(),
                    TokenType.Symbol,
                    _reader.Position,
                    1));

                _reader.MoveNext();
            }
        }

        return tokens;
    }
}