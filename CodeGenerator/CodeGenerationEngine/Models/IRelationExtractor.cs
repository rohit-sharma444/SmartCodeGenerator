using CodeGenerationEngine.Models;
using CodeGenerationEngine.Semantics.Models;

namespace CodeGenerationEngine.Semantics;

public interface IRelationExtractor
{
    IEnumerable<SemanticRelation> Extract(
        IReadOnlyList<Token> tokens);
}