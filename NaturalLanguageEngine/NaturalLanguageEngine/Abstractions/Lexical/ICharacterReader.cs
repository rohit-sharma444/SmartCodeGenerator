namespace NaturalLanguageEngine.Abstractions.Lexical;

public interface ICharacterReader
{
    bool EndOfInput { get; }

    int Position { get; }

    char Current { get; }

    char Peek(int offset = 1);

    bool MoveNext();

    void Reset(string text);
}