using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Abstractions.Context;

public interface IContextManager
{
    SemanticResult ApplyContext(
        SemanticResult current);
}