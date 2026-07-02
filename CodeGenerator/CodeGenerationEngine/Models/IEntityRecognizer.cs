using CodeGenerationEngine.Models;
using CodeGenerationEngine.Semantics.Models;

namespace CodeGenerationEngine.Semantics;

public interface IEntityRecognizer
{
    IEnumerable<SemanticEntity> Extract(
        IReadOnlyList<Token> tokens);
}