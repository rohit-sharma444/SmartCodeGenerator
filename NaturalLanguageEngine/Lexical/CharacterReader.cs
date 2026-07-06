using NaturalLanguageEngine.Abstractions.Lexical;

namespace NaturalLanguageEngine.Lexical;

/// <summary>
/// Represents a reader that reads characters from a string input.
/// </summary>
public sealed class CharacterReader : ICharacterReader
{
    private ReadOnlyMemory<char> _buffer = ReadOnlyMemory<char>.Empty;

    private int _position = -1;
    
    public int Position => _position;

    public bool EndOfInput => _position >= _buffer.Length;

    public char Current
    {
        get
        {
            if (_position < 0 || _position >= _buffer.Length)
                return '\0';

            return _buffer.Span[_position];
        }
    }

    public void Reset(string text)
    {
        _buffer = text.AsMemory();
        _position = -1;
    }

    public bool MoveNext()
    {
        if (_position >= _buffer.Length)
            return false;

        _position++;

        return _position < _buffer.Length;
    }

    public char Peek(int offset = 1)
    {
        int index = _position + offset;

        if (index < 0 || index >= _buffer.Length)
            return '\0';

        return _buffer.Span[index];
    }
}