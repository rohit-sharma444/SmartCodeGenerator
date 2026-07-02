namespace NaturalLanguageEngine.Models.Semantics;

public sealed class Relationship
{
    public Entity Source { get; set; } = default!;

    public Entity Target { get; set; } = default!;

    public RelationshipType Type { get; set; }
}