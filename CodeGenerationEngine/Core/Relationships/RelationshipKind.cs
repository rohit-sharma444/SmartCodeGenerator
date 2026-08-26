namespace CodeGenerationEngine.Core.Relationships;

/// <summary>How two classes are related, which decides which side carries the foreign key
/// and what the inverse property looks like.</summary>
public enum RelationshipKind
{
    /// <summary>The owner carries the foreign key; the target gets a collection.</summary>
    OneToMany,

    /// <summary>The owner carries the foreign key; the target gets a single reference.</summary>
    OneToOne,

    /// <summary>Neither side carries a foreign key. A join class does, and both sides get a
    /// collection of it.</summary>
    ManyToMany
}
