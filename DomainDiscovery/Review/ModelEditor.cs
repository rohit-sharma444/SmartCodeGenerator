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

    public static bool AddEntity(CandidateDomainModel model, string name)
    {
        var normalized = NameNormalizer.ToEntityName(name);
        if (normalized.Length == 0 || Find(model, normalized) is not null)
            return false;

        model.Entities.Add(DefaultFields.ApplyTo(
            new CandidateEntity { Name = normalized, Provenance = Provenance.Seed },
            Provenance.Seed));
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
        if (normalized.Length == 0 || FindField(owner, normalized) is not null)
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
    /// Removes <paramref name="fieldName"/> from <paramref name="entityName"/>. The default
    /// <see cref="DefaultFields.NameField"/> cannot be removed: without it an entity scaffolds
    /// a grid whose only column is a read-only Id, leaving nothing to type into.
    /// </summary>
    public static bool RemoveField(CandidateDomainModel model, string entityName, string fieldName)
    {
        var normalized = NameNormalizer.ToFieldName(fieldName);
        if (string.Equals(normalized, DefaultFields.NameField, StringComparison.OrdinalIgnoreCase))
            return false;

        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        var field = owner is null ? null : FindField(owner, normalized);

        if (owner is null || field is null)
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
