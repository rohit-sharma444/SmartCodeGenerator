namespace DomainDiscovery.Model;

/// <summary>
/// The fallback shape for an entity the <c>DomainFieldLexicon</c> does not know.
/// </summary>
/// <remarks>
/// Discovered entities are shaped from the lexicon and dropped when it has nothing to say about
/// them. An entity the user names by hand is different: they have asserted it exists, so it is
/// kept, and it needs one editable column or its grid shows nothing but a read-only Id.
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
