using DomainDiscovery.Enrichment;

namespace DomainDiscovery.Model;

/// <summary>Where a discovered entity or field came from.</summary>
public enum Provenance
{
    /// <summary>Extracted from the user's own text.</summary>
    Seed,

    /// <summary>Harvested from a fetched Wikipedia article.</summary>
    Wiki,

    /// <summary>
    /// Read from a data source the user supplied — a CSV column, a JSON property, a SQL column.
    /// Distinct from <see cref="Seed"/> because the review loop prints this verbatim, and calling
    /// a real column "Seed" would claim the pipeline guessed at something it was told.
    /// </summary>
    Data
}

public sealed class CandidateField
{
    /// <summary>
    /// Settable for the same reason <see cref="CandidateEntity.Name"/> is: the review loop lets
    /// the user rename a property, and every other reference to this field is a lookup by name
    /// rather than a held reference.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Settable so the review loop can overrule a type the pipeline inferred. Where the name came
    /// from does not change when its declared type does, which is why
    /// <see cref="Provenance"/> below stays init-only.
    /// </summary>
    public required string Type { get; set; }

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

    /// <summary>
    /// What kind of thing this class is. Exists to decide which way a mined relationship
    /// points: same-sentence co-occurrence between two classes is symmetric (it says only that
    /// they were mentioned together), but a foreign key has a direction, and comparing two
    /// classes' Role settles it — the higher-ranked role references the lower. Defaults to
    /// <see cref="EntityRole.None"/>, which also means "never link this class" for anything
    /// <see cref="Enrichment.EntityShaper"/> never assigned a Role to.
    /// </summary>
    public EntityRole Role { get; set; }
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

    /// <summary>
    /// Why each candidate met the fate it did. Append-only, and never read by scaffolding —
    /// this is the audit trail behind the review loop's <c>why</c> verb.
    /// </summary>
    /// <remarks>
    /// It lives on the model rather than in an observer because
    /// <see cref="Enrichment.DomainEnricher"/> mutates and returns the seed model, so this one
    /// instance already flows from seed extraction through to the review loop. A test can then
    /// assert the trail as a value on the result rather than by intercepting a callback.
    /// </remarks>
    public List<DecisionRecord> Decisions { get; } = new();

    /// <summary>Appends one decision. See <see cref="DecisionReasons"/> for
    /// <paramref name="reason"/>, which must be one of those constants.</summary>
    public void Record(
        string name,
        DecisionStage stage,
        DecisionOutcome outcome,
        string reason,
        string? evidence = null)
        => Decisions.Add(new DecisionRecord
        {
            Name = name,
            Stage = stage,
            Outcome = outcome,
            Reason = reason,
            Evidence = evidence
        });
}
