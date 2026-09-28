using System.Text.RegularExpressions;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
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
        => RemoveField(model, entityName, fieldName, out _);

    /// <inheritdoc cref="RemoveField(CandidateDomainModel, string, string)"/>
    /// <param name="droppedRules">
    /// The accepted rules that went with the field, as their descriptions. A rule naming a
    /// property the class no longer has cannot be enforced, so it goes - but it was the user's
    /// choice, and the caller says so rather than let it vanish.
    /// </param>
    public static bool RemoveField(
        CandidateDomainModel model, string entityName, string fieldName,
        out IReadOnlyList<string> droppedRules)
    {
        droppedRules = [];

        var normalized = NameNormalizer.ToFieldName(fieldName);
        if (string.Equals(normalized, DefaultFields.NameField, StringComparison.OrdinalIgnoreCase))
            return false;

        var owner = Find(model, NameNormalizer.ToEntityName(entityName));
        var field = owner is null ? null : FindField(owner, normalized);

        if (owner is null || field is null || owner.Fields.Count == 1)
            return false;

        owner.Fields.Remove(field);

        var naming = owner.Proposals.Where(p => Names(p, field.Name)).ToList();
        droppedRules = [.. naming.Where(p => p.Accepted).Select(p => p.Rule.Describe())];

        foreach (var proposal in naming)
            owner.Proposals.Remove(proposal);

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

        return TryRename(owner, field, NameNormalizer.ToFieldName(newName));
    }

    /// <summary>
    /// The rename behind <see cref="RenameField"/>, for a name already normalized - which is also
    /// how <see cref="RenameEntity"/> moves a foreign key onto its target's new name.
    /// </summary>
    private static bool TryRename(CandidateEntity owner, CandidateField field, string normalized)
    {
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

        var former = field.Name;
        field.Name = normalized;

        for (var i = 0; i < owner.Proposals.Count; i++)
        {
            if (Names(owner.Proposals[i], former)
                && Renamed(owner.Proposals[i], former, normalized) is { } carried)
                owner.Proposals[i] = carried;
        }

        return true;
    }

    private static bool Names(EntityRuleProposal proposal, string fieldName)
        => proposal.Properties.Any(
            p => string.Equals(p, fieldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <paramref name="proposal"/> with <paramref name="from"/> renamed to <paramref name="to"/>
    /// throughout - its properties, its rule and the text on its card - or null for a rule this
    /// cannot rebuild.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than left alone because a rule holds its property names, and the generated
    /// validator skips one naming a property the class no longer has: a rename used to drop an
    /// accepted rule without a word. The three kinds are the ones
    /// <c>EntityRuleProposals</c> builds, and the texts are replaced whole-word and
    /// case-sensitively because they are spelled from the field names themselves.
    /// </remarks>
    private static EntityRuleProposal? Renamed(EntityRuleProposal proposal, string from, string to)
    {
        string Name(string name)
            => string.Equals(name, from, StringComparison.OrdinalIgnoreCase) ? to : name;

        var spelled = proposal.Properties.First(
            p => string.Equals(p, from, StringComparison.OrdinalIgnoreCase));

        string Text(string text)
            => Regex.Replace(text, $@"\b{Regex.Escape(spelled)}\b", to);

        var names = proposal.Rule.Properties.Select(Name).ToList();

        IValidationRule? rule = proposal.Rule switch
        {
            ComparisonRule comparison => new ComparisonRule(
                names[0], names[1], comparison.ViolationOperator, Text(comparison.Describe())),
            ArithmeticRule arithmetic => new ArithmeticRule(
                names[0], names[1], arithmetic.Operator, names[2]),
            PercentageRule => new PercentageRule(names[0], names[1], names[2]),
            _ => null,
        };

        if (rule is null)
            return null;

        return new EntityRuleProposal
        {
            Kind = proposal.Kind,
            Properties = [.. proposal.Properties.Select(Name)],
            Rule = rule,
            Evidence = Text(proposal.Evidence),
            Accepted = proposal.Accepted
        };
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

        // Without this, `why <name>` on a class the user removed says "No decision recorded" --
        // which reads as the tool never having considered it, when removing it was the decision.
        model.Record(
            target.Name, DecisionStage.ReviewLoop, DecisionOutcome.Skipped,
            DecisionReasons.YouRemovedIt);

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

        FollowReferences(model, former, normalizedNew);

        // The trail is filed under the name each stage saw, so a rename strands it: `why` on the
        // new name would find nothing and report a discovered entity as hand-added. Recording
        // both directions lets the user follow the rename from whichever name they remember.
        model.Record(normalizedNew, DecisionStage.ReviewLoop, DecisionOutcome.Kept,
            DecisionReasons.YouRenamedIt, $"renamed from \"{former}\"");
        model.Record(former, DecisionStage.ReviewLoop, DecisionOutcome.Skipped,
            DecisionReasons.YouRenamedIt, $"renamed to \"{normalizedNew}\"");

        return true;
    }

    /// <summary>
    /// Points every relationship aimed at <paramref name="former"/> at <paramref name="renamed"/>,
    /// and moves its <c>&lt;Target&gt;Id</c> key with it.
    /// </summary>
    /// <remarks>
    /// Both halves, for the reason <see cref="CandidateReference"/> records both: the mapper looks
    /// a Target up by the class's current name and skips one it cannot find, and
    /// <c>ForeignKeys.TargetsOf</c> knows a reference only by its key field. Left behind, the
    /// renamed class lost every reference to it without a word. A key that cannot take the new
    /// name - the entity already has a field called that - keeps the old one rather than collide.
    /// </remarks>
    private static void FollowReferences(CandidateDomainModel model, string former, string renamed)
    {
        foreach (var entity in model.Entities)
        {
            for (var i = 0; i < entity.Relationships.Count; i++)
            {
                var relationship = entity.Relationships[i];
                if (!string.Equals(relationship.Target, former, StringComparison.OrdinalIgnoreCase))
                    continue;

                entity.Relationships[i] = new CandidateRelationship
                {
                    Target = renamed,
                    Role = relationship.Role
                };

                if (FindField(entity, $"{former}Id") is { } key)
                    TryRename(entity, key, $"{renamed}Id");
            }
        }
    }

    private static CandidateEntity? Find(CandidateDomainModel model, string name)
        => model.Entities.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
