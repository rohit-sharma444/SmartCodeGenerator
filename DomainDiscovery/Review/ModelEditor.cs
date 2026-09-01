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
    /// <remarks>
    /// Public, and ordered, because every control that offers a type is built from it. The
    /// review screen used to hard-code the same five in its own mark-up under a comment warning
    /// that a sixth would compile and then fail at run time - which is a copy waiting to drift,
    /// and there are three such controls now.
    /// </remarks>
    public static IReadOnlyList<string> SupportedTypes { get; } =
        ["string", "int", "decimal", "bool", "DateTime"];

    private static readonly HashSet<string> Supported =
        new(SupportedTypes, StringComparer.Ordinal);

    /// <summary>
    /// Adds an entity the user named. It is shaped from <see cref="DomainFieldLexicon"/> when
    /// that knows it, and otherwise gets the fallback <c>Name</c> column — unlike a discovered
    /// entity, one the user asked for by name is never dropped. If a suggestion matches the name,
    /// it is promoted to the model as-is, keeping its provenance and confidence intact.
    /// </summary>
    public static bool AddEntity(CandidateDomainModel model, string name)
        => AddEntity(model, name, out _);

    /// <inheritdoc cref="AddEntity(CandidateDomainModel, string)"/>
    /// <param name="addedName">
    /// The name the model actually received, or null if nothing was added. Promoting a suggestion
    /// keeps that suggestion's own name, so this is not always what the normalizer returned for
    /// <paramref name="name"/> — which is why the caller cannot work it out for itself.
    /// </param>
    public static bool AddEntity(CandidateDomainModel model, string name, out string? addedName)
    {
        addedName = null;

        // The name came from a person, not from prose: keep the shape they typed.
        var normalized = NameNormalizer.ToUserTypedEntityName(name);
        if (!NameNormalizer.IsValidIdentifier(normalized) || Find(model, normalized) is not null)
            return false;

        // Promoting a suggestion is not the same as inventing an entity: keep what discovery
        // already learned about it.
        var suggested = model.Suggestions.FirstOrDefault(
            e => string.Equals(e.Name, normalized, StringComparison.OrdinalIgnoreCase));

        if (suggested is not null)
        {
            model.Suggestions.Remove(suggested);

            foreach (var shape in DomainFieldLexicon.Fields(normalized))
                suggested.Fields.Add(new CandidateField
                {
                    Name = shape.Name,
                    Type = shape.Type,
                    Provenance = suggested.Provenance
                });

            if (suggested.Fields.Count == 0)
                DefaultFields.ApplyTo(suggested, suggested.Provenance);

            model.Entities.Add(suggested);
            addedName = suggested.Name;
            return true;
        }

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
        addedName = entity.Name;
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
        if (!Supported.Contains(resolved))
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

    /// <summary>
    /// Renames <paramref name="fieldName"/> on <paramref name="entityName"/>, keeping the type
    /// and provenance it already had.
    /// </summary>
    /// <remarks>
    /// The new name has to clear everything <see cref="AddField"/> demands of one, because the
    /// class this ends up in cannot tell how the property got its name: a valid identifier, not
    /// already taken, not <c>Id</c> (generated, and assigned by reflection), and not the entity's
    /// own name (CS0542).
    /// <para>
    /// Renaming <see cref="DefaultFields.NameField"/> is allowed even though
    /// <see cref="RemoveField"/> refuses to delete it. That guard exists because deleting it can
    /// leave a grid whose only column is a read-only Id; a rename leaves a column standing, so it
    /// is a different question with a different answer.
    /// </para>
    /// </remarks>
    public static bool RenameField(
        CandidateDomainModel model, string entityName, string fieldName, string newName)
    {
        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        var field = owner is null ? null : FindField(owner, NameNormalizer.ToFieldName(fieldName));

        if (owner is null || field is null)
            return false;

        var normalized = NameNormalizer.ToFieldName(newName);
        if (!NameNormalizer.IsValidIdentifier(normalized))
            return false;

        // Nothing to do, and saying otherwise would report an edit that did not happen. Checked
        // before the collision test below, which the field would otherwise fail against itself.
        if (string.Equals(normalized, field.Name, StringComparison.Ordinal))
            return false;

        if (FindField(owner, normalized) is not null)
            return false;

        if (string.Equals(normalized, "Id", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(normalized, owner.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        field.Name = normalized;
        return true;
    }

    /// <summary>
    /// Declares <paramref name="fieldName"/> as <paramref name="type"/> instead of whatever
    /// <see cref="SeedExtraction.TypeInference"/> guessed for it.
    /// </summary>
    /// <remarks>
    /// Returns false for a type the field already has: it is not a failure, but it is not a
    /// change either, and every caller of this class reports what actually happened.
    /// </remarks>
    public static bool ChangeFieldType(
        CandidateDomainModel model, string entityName, string fieldName, string type)
    {
        if (!Supported.Contains(type))
            return false;

        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        var field = owner is null ? null : FindField(owner, NameNormalizer.ToFieldName(fieldName));

        if (field is null || string.Equals(field.Type, type, StringComparison.Ordinal))
            return false;

        field.Type = type;
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
        // The old name is a lookup, so it keeps the head-noun normalizer; the new one is a class
        // name the user typed, so it keeps its shape.
        var target = Find(model, NameNormalizer.ToEntityName(oldName));
        var normalizedNew = NameNormalizer.ToUserTypedEntityName(newName);
        if (target is null || normalizedNew.Length == 0 || Find(model, normalizedNew) is not null)
            return false;

        var former = target.Name;
        target.Name = normalizedNew;

        // The trail is filed under the name each stage saw, so a rename strands it: `why` on the
        // new name would find nothing and report a discovered entity as hand-added. Recording
        // both directions lets the user follow the rename from whichever name they remember.
        model.Record(normalizedNew, DecisionStage.ReviewLoop, DecisionOutcome.Kept,
            DecisionReasons.YouRenamedIt, $"renamed from \"{former}\"");
        model.Record(former, DecisionStage.ReviewLoop, DecisionOutcome.Skipped,
            DecisionReasons.YouRenamedIt, $"renamed to \"{normalizedNew}\"");

        return true;
    }

    private static CandidateEntity? Find(CandidateDomainModel model, string name)
        => model.Entities.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
