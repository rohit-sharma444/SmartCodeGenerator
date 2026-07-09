namespace DomainDiscovery.Model;

/// <summary>
/// The fields every candidate entity starts with, wherever it was created.
/// </summary>
/// <remarks>
/// Nothing in the pipeline can mine fields from prose. The NLE never tags an entity
/// <c>Attribute</c>, and the attribute-shaped patterns that do occur ("<i>X</i> of <i>Y</i>",
/// possessives) resolve to adjectives and sentence fragments far too often to generate a class
/// from. An entity with no fields scaffolds a grid whose only column is a read-only Id, so
/// every entity gets one editable string column and the review loop is where the rest are added.
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
