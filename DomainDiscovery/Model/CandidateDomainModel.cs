namespace DomainDiscovery.Model;

/// <summary>Where a discovered entity or field came from.</summary>
public enum Provenance
{
    /// <summary>Extracted from the user's own text.</summary>
    Seed,

    /// <summary>Harvested from a fetched Wikipedia article.</summary>
    Wiki
}

public sealed class CandidateField
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Provenance Provenance { get; init; }
}

public sealed class CandidateRelationship
{
    public required string Target { get; init; }
    public required string Role { get; init; }
}

public sealed class CandidateEntity
{
    public required string Name { get; set; }
    public List<CandidateField> Fields { get; } = new();
    public List<CandidateRelationship> Relationships { get; } = new();
    public Provenance Provenance { get; set; }
    public double Confidence { get; set; }

    /// <summary>
    /// The Wikipedia article this candidate came from, when its title differs from its type
    /// name ("Cash register" -> CashRegister). Null for a prose-harvested noun, whose name
    /// is already the title to look up.
    /// </summary>
    public string? SourceTitle { get; set; }
}

public sealed class CandidateDomainModel
{
    public List<CandidateEntity> Entities { get; } = new();

    /// <summary>
    /// Candidates we could not shape. They are offered to the user rather than deleted:
    /// not knowing an entity's fields is not the same as knowing it is not an entity.
    /// </summary>
    public List<CandidateEntity> Suggestions { get; } = new();

    /// <summary>
    /// Names rejected as prose — their definition resolved to an abstraction. Shown once,
    /// so a missing class is explainable rather than mysterious.
    /// </summary>
    public List<string> Rejected { get; } = new();
}
