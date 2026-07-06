using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Abstractions.Semantics;

public interface IRelationshipExtractor
{
    IReadOnlyList<Relationship> Extract(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Dependency> dependencies);
}