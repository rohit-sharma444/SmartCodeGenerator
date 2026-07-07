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
}

public sealed class CandidateDomainModel
{
    public List<CandidateEntity> Entities { get; } = new();
}
