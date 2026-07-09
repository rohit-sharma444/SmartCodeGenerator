using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Review;

/// <summary>
/// Pure mutations behind the interactive review loop. Each method returns whether
/// it changed the model, so the CLI can report the outcome.
/// </summary>
public static class ModelEditor
{
    /// <summary>
    /// Types a generated property may declare. An unknown type would not compile, and the
    /// review loop is the last place to catch it before Roslyn emits the class.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "string", "int", "decimal", "bool", "DateTime"
    };

    /// <summary>
    /// Adds an entity the user named. It is shaped from <see cref="DomainFieldLexicon"/> when
    /// that knows it, and otherwise gets the fallback <c>Name</c> column — unlike a discovered
    /// entity, one the user asked for by name is never dropped.
    /// </summary>
    public static bool AddEntity(CandidateDomainModel model, string name)
    {
        var normalized = NameNormalizer.ToEntityName(name);
        if (!NameNormalizer.IsValidIdentifier(normalized) || Find(model, normalized) is not null)
            return false;

        var entity = new CandidateEntity { Name = normalized, Provenance = Provenance.Seed };
        foreach (var shape in DomainFieldLexicon.Fields(normalized))
            entity.Fields.Add(new CandidateField
            {
                Name = shape.Name,
                Type = shape.Type,
                Provenance = Provenance.Seed
            });

        if (entity.Fields.Count == 0)
            DefaultFields.ApplyTo(entity, Provenance.Seed);

        model.Entities.Add(entity);
        return true;
    }

    /// <summary>
    /// Adds <paramref name="fieldName"/> to the entity named <paramref name="entityName"/>.
    /// <paramref name="type"/> is inferred from the field name when null; passing it explicitly
    /// is how a user overrides a wrong guess ("Servings" infers string, but means int).
    /// </summary>
    public static bool AddField(
        CandidateDomainModel model, string entityName, string fieldName, string? type = null)
    {
        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        if (owner is null)
            return false;

        var normalized = NameNormalizer.ToFieldName(fieldName);
        if (!NameNormalizer.IsValidIdentifier(normalized) || FindField(owner, normalized) is not null)
            return false;

        // Id is generated, not declared: DomainModelMapper prepends `int Id` only when no Id
        // field exists, so a string Id compiles and then JsonStore, which assigns Id by
        // reflection, throws the first time the user clicks Add.
        if (string.Equals(normalized, "Id", StringComparison.OrdinalIgnoreCase))
            return false;

        // `class Recipe { public string Recipe { get; set; } }` is CS0542.
        if (string.Equals(normalized, owner.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        var resolved = type ?? TypeInference.Infer(normalized);
        if (!SupportedTypes.Contains(resolved))
            return false;

        owner.Fields.Add(new CandidateField
        {
            Name = normalized,
            Type = resolved,
            Provenance = Provenance.Seed
        });
        return true;
    }

    /// <summary>
    /// Removes <paramref name="fieldName"/> from <paramref name="entityName"/>. Two fields
    /// cannot be removed, because both leave a grid whose only column is a read-only Id:
    /// <see cref="DefaultFields.NameField"/>, and whichever field is the entity's last.
    /// </summary>
    public static bool RemoveField(CandidateDomainModel model, string entityName, string fieldName)
    {
        var normalized = NameNormalizer.ToFieldName(fieldName);
        if (string.Equals(normalized, DefaultFields.NameField, StringComparison.OrdinalIgnoreCase))
            return false;

        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        var field = owner is null ? null : FindField(owner, normalized);

        if (owner is null || field is null || owner.Fields.Count == 1)
            return false;

        owner.Fields.Remove(field);
        return true;
    }

    private static CandidateField? FindField(CandidateEntity entity, string fieldName)
        => entity.Fields.FirstOrDefault(
            f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));

    public static bool RemoveEntity(CandidateDomainModel model, string name)
    {
        var target = Find(model, NameNormalizer.ToEntityName(name));
        if (target is null)
            return false;

        model.Entities.Remove(target);
        return true;
    }

    public static bool RenameEntity(CandidateDomainModel model, string oldName, string newName)
    {
        var target = Find(model, NameNormalizer.ToEntityName(oldName));
        var normalizedNew = NameNormalizer.ToEntityName(newName);
        if (target is null || normalizedNew.Length == 0 || Find(model, normalizedNew) is not null)
            return false;

        target.Name = normalizedNew;
        return true;
    }

    private static CandidateEntity? Find(CandidateDomainModel model, string name)
        => model.Entities.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
