namespace DomainDiscovery.Model;

/// <summary>
/// The fallback shape for an entity the <c>DomainFieldLexicon</c> does not know.
/// </summary>
/// <remarks>
/// A discovered entity is no longer dropped when <c>DomainFieldLexicon</c> has nothing to say
/// about it: <see cref="DomainDiscovery.Enrichment.EntityShaper"/> falls back to the candidate's
/// own Wikipedia definition, and only rejects it outright when that definition is itself an
/// abstraction — otherwise it is offered to the user as a suggestion, never silently deleted.
/// This fallback exists for exactly the opposite case: an entity the user names by hand in the
/// review loop has no Wikipedia definition to fall back to, and unlike a discovered candidate it
/// was never a guess — the user asserted it exists, so
/// <see cref="DomainDiscovery.Review.ModelEditor.AddEntity"/> keeps it unconditionally and gives
/// it this one field, or its grid would show nothing but a read-only Id.
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
