using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Abstractions.Syntax;

public interface IDependencyParser
{
    IReadOnlyList<Dependency> Parse(
        IReadOnlyList<Phrase> phrases);
}