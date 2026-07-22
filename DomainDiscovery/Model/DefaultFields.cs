namespace DomainDiscovery.Model;

/// <summary>
/// The fallback shape for an entity the <c>DomainFieldLexicon</c> does not know.
/// </summary>
/// <remarks>
/// <see cref="DomainDiscovery.Enrichment.EntityShaper"/> applies this fallback in two cases.
/// First, an entity the user names by hand in the review loop: unlike a discovered candidate,
/// the user asserted it exists, so <see cref="DomainDiscovery.Review.ModelEditor.AddEntity"/>
/// gives it this one field unconditionally — otherwise its scaffolded grid would show nothing but
/// a read-only Id. Second, a discovered seed candidate with an <see cref="ShapeVerdict.Unknown"/>
/// verdict: the entity was lifted from the user's own typed phrase, so where the archetype table
/// has no opinion, the user having named the word is evidence enough to keep it. In both cases
/// the single <c>Name</c> field ensures the grid is editable and the entity is not silent.
/// </remarks>
public static class DefaultFields
{
    /// <summary>The one field every entity carries, so a scaffolded grid is editable.</summary>
    public const string NameField = "Name";

    /// <summary>
    /// Adds the default <c>Name</c> field to <paramref name="entity"/> unless it already
    /// declares one. Attributed to <paramref name="provenance"/> so the review loop can
    /// show where it came from.
    /// </summary>
    public static CandidateEntity ApplyTo(CandidateEntity entity, Provenance provenance)
    {
        if (!entity.Fields.Any(
                f => string.Equals(f.Name, NameField, StringComparison.OrdinalIgnoreCase)))
        {
            entity.Fields.Add(new CandidateField
            {
                Name = NameField,
                Type = "string",
                Provenance = provenance
            });
        }

        return entity;
    }
}
