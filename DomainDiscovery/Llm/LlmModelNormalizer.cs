using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Llm;

/// <summary>
/// Turns what a language model said into something the rest of the tool can compile.
/// </summary>
/// <remarks>
/// Nothing the model returns is trusted. Measured on the two installed 3B models:
/// llama3.2:3b emitted <c>customer_id</c>, <c>order_date</c> and the type <c>datetime</c>;
/// qwen2.5:3b emitted a <c>SaleItem</c> with no key at all.
/// <para>
/// This class is deterministic, which is the point: the LLM call cannot carry test coverage,
/// so every rule worth asserting lives here instead.
/// </para>
/// </remarks>
public static class LlmModelNormalizer
{
    public static CandidateDomainModel Normalize(LlmDomainModel raw)
    {
        var model = new CandidateDomainModel();

        // Pass 1: names and roles. Relationships need every class's role before any of them can
        // be judged, so no relationship can be settled until this pass has finished.
        var accepted = new List<(LlmClass Raw, CandidateEntity Entity)>();

        foreach (var llmClass in raw.Classes)
        {
            // ToUserTypedEntityName, NOT ToEntityName. ToEntityName reduces a phrase to its head
            // noun and lowercases everything after the first character, which turns SaleItem into
            // "Saleitem" and OrderItem into "Orderitem" — and those are the exact class names both
            // measured models produce. This one preserves a compound the way it was written, which
            // is what its own summary says it exists for. It still rejects "2nd course".
            var name = NameNormalizer.ToUserTypedEntityName(llmClass.Name);

            if (!NameNormalizer.IsValidIdentifier(name))
            {
                model.Rejected.Add(llmClass.Name);
                model.Record(
                    llmClass.Name,
                    DecisionStage.ModelProposal,
                    DecisionOutcome.Rejected,
                    DecisionReasons.TheModelGaveNoUsableName);
                continue;
            }

            if (accepted.Any(a => a.Entity.Name == name))
            {
                // A second class normalizing to the same name would otherwise vanish with no
                // trail at all — the invalid-identifier branch above both rejects and records;
                // this path must match it, or `why <Class>` goes unanswerable for it.
                model.Rejected.Add(llmClass.Name);
                model.Record(
                    llmClass.Name,
                    DecisionStage.ModelProposal,
                    DecisionOutcome.Rejected,
                    DecisionReasons.AlreadyNamed);
                continue;
            }

            var entity = new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Model,
                Confidence = 1d,
                Role = ParseRole(llmClass.Role)
            };

            model.Record(
                name,
                DecisionStage.ModelProposal,
                DecisionOutcome.Kept,
                DecisionReasons.ProposedByTheModel,
                string.IsNullOrWhiteSpace(llmClass.Purpose) ? null : llmClass.Purpose.Trim());

            accepted.Add((llmClass, entity));
        }

        var byName = accepted.ToDictionary(a => a.Entity.Name, a => a.Entity);

        // Pass 2: properties and relationships, now that every role is known.
        foreach (var (llmClass, entity) in accepted)
        {
            foreach (var property in llmClass.Properties)
                AddProperty(entity, property, byName);

            foreach (var relationship in llmClass.Relationships)
            {
                // The same normalizer the class names went through, or the lookup misses.
                var target = NameNormalizer.ToUserTypedEntityName(relationship.Target);

                if (target == entity.Name || !byName.TryGetValue(target, out var targetEntity))
                    continue;

                Link(entity, targetEntity, string.IsNullOrWhiteSpace(relationship.Role)
                    ? "has"
                    : relationship.Role.Trim());
            }

            EnsureKey(entity);

            // Every other path into CandidateDomainModel guarantees an editable grid:
            // EntityShaper does it for a discovered class, DataModelBuilder for an imported one,
            // ModelEditor for one the user adds by hand. This path bypasses EntityShaper, so it
            // is the only one that could hand scaffolding a class whose grid shows nothing but a
            // read-only Id — exactly what DefaultFields was written to prevent. Applied only when
            // the model actually left the class empty; a class with real fields keeps them alone,
            // because an Order legitimately has no Name.
            if (!entity.Fields.Any(f => !IsKeyOf(f.Name, entity.Name)))
                DefaultFields.ApplyTo(entity, Provenance.Model);

            model.Entities.Add(entity);
        }

        return model;
    }

    /// <summary>
    /// Adds one property — unless it is a foreign key naming another class, in which case it
    /// becomes a relationship instead of a stray int sitting beside one that means the same thing.
    /// </summary>
    private static void AddProperty(
        CandidateEntity entity,
        LlmProperty property,
        IReadOnlyDictionary<string, CandidateEntity> byName)
    {
        var name = NameNormalizer.ToFieldName(property.Name);

        if (!NameNormalizer.IsValidIdentifier(name) || name == entity.Name)
            return;

        if (entity.Fields.Any(f => f.Name == name))
            return;

        if (TryForeignKeyTarget(name, byName, out var target)
            && target.Name != entity.Name
            && Link(entity, target, "has"))
        {
            // Became a relationship; the scalar would now be a duplicate of it.
            return;
        }

        entity.Fields.Add(new CandidateField
        {
            Name = name,
            Type = ToCsharpType(property.Type),
            Provenance = Provenance.Model
        });
    }

    /// <summary>
    /// Whether <paramref name="fieldName"/> is a <c>CustomerId</c>-shaped reference to a class
    /// the model also defined. "Id" alone is a key, not a reference.
    /// </summary>
    private static bool TryForeignKeyTarget(
        string fieldName,
        IReadOnlyDictionary<string, CandidateEntity> byName,
        out CandidateEntity target)
    {
        target = null!;

        // OrdinalIgnoreCase, not Ordinal: the DEFAULT model writes SaleID and ProductID, not
        // SaleId. An exact-case check would miss every foreign key qwen2.5:3b produces.
        if (!fieldName.EndsWith("id", StringComparison.OrdinalIgnoreCase) || fieldName.Length <= 2)
            return false;

        return byName.TryGetValue(fieldName[..^2], out target!);
    }

    /// <summary>
    /// Whether <paramref name="fieldName"/> is this class's own key. Both spellings a model
    /// actually produces count: a bare "Id", and the "SaleID" form qwen2.5:3b writes on class Sale.
    /// </summary>
    private static bool IsKeyOf(string fieldName, string entityName)
        => fieldName.Equals("Id", StringComparison.OrdinalIgnoreCase)
           || fieldName.Equals($"{entityName}Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a relationship if — and only if — the existing role rules allow it.
    /// </summary>
    /// <remarks>
    /// <see cref="EntityRoleRank.CanReference"/> is the gate a real run needed: relying on rank
    /// alone once produced Restaurant.CustomerId and CashRegister.CustomerId. A language model
    /// proposes the same shapes, so the rule is applied to its output rather than replaced by it.
    /// </remarks>
    /// <returns>True when the relationship was added.</returns>
    private static bool Link(CandidateEntity from, CandidateEntity to, string role)
    {
        if (!EntityRoleRank.CanReference(from.Role))
            return false;

        if (EntityRoleRank.RankOf(from.Role) <= EntityRoleRank.RankOf(to.Role))
            return false;

        if (from.Relationships.Any(r => r.Target == to.Name))
            return false;

        from.Relationships.Add(new CandidateRelationship { Target = to.Name, Role = role });
        return true;
    }

    private static void EnsureKey(CandidateEntity entity)
    {
        // IsKeyOf, not f.Name == "Id": qwen2.5:3b names Sale's key "SaleID", and an exact match
        // would add a second key beside the one the model already gave us.
        if (entity.Fields.Any(f => IsKeyOf(f.Name, entity.Name)))
            return;

        // A class itself named "Id" must not get a field named "Id" inserted here: that emits
        // `class Id { public int Id { get; set; } }`, which is CS0542 and will not compile.
        // DefaultFields.ApplyTo carries the identical guard for its own "Name" field; this is
        // its analogue for the key. The class still gets an editable grid, because
        // DefaultFields.ApplyTo runs afterward and gives it a Name field instead.
        if (string.Equals(entity.Name, "Id", StringComparison.OrdinalIgnoreCase))
            return;

        entity.Fields.Insert(0, new CandidateField
        {
            Name = "Id",
            Type = "int",
            Provenance = Provenance.Model
        });
    }

    private static EntityRole ParseRole(string role)
        => Enum.TryParse<EntityRole>(role?.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : EntityRole.None;

    /// <summary>
    /// A real C# type for whatever the model called it. Anything unrecognized becomes
    /// <c>string</c> rather than an invented type name that would not compile.
    /// </summary>
    private static string ToCsharpType(string type) => (type ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "int" or "integer" or "int32" or "number" => "int",
        "long" or "int64" or "bigint" => "long",
        "decimal" or "money" or "currency" or "numeric" => "decimal",
        "double" or "float" or "real" => "double",
        "bool" or "boolean" or "bit" => "bool",
        "datetime" or "date" or "timestamp" or "datetime2" => "DateTime",
        "guid" or "uuid" or "uniqueidentifier" => "Guid",
        _ => "string"
    };
}
