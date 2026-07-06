using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Semantics;

/// <summary>
/// Extracts subject/object/modifier relationships between entities.
///
/// <para>
/// Prefers a real syntactic dependency parse (see <see
/// cref="NaturalLanguageEngine.Abstractions.Syntax.IDependencyParser"/>,
/// wired in by <see cref="NaturalLanguageEngine.Core.Stages.DependencyParsingStage"/>)
/// when one is available: each <see cref="Dependency"/> edge is mapped back
/// to the entities its governor/dependent tokens produced (via
/// <see cref="NaturalLanguageEngine.Models.Analysis.TokenAnalysis.Entity"/>,
/// set by <see cref="EntityRecognizer"/>) and translated into a
/// <see cref="Relationship"/>. This correctly follows grammatical roles
/// rather than raw entity order, so it holds up when a sentence has more
/// than one action or more than a couple of entities.
/// </para>
///
/// <para>
/// Falls back to a purely positional heuristic - <c>[entity before the
/// action] = Subject</c>, <c>[entities after the action] =
/// Object/Recipient/Time/Location</c> - when no dependency edges are
/// supplied, or when the supplied edges don't resolve to any entity-level
/// relationships (e.g. a sentence fragment with entities but no verb
/// phrase for the parser to anchor a clause on). That heuristic remains
/// useful for such inputs, but is known to misread passive voice,
/// coordinated subjects/objects, negation, and questions - the same
/// caveats documented on <see
/// cref="NaturalLanguageEngine.Syntax.DependencyParser"/> apply to it, plus
/// its own well-known weakness of only ever anchoring on the first action
/// entity.
/// </para>
/// </summary>
public sealed class RelationshipExtractor : IRelationshipExtractor
{
    public IReadOnlyList<Relationship> Extract(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Dependency> dependencies)
    {
        if (dependencies.Count > 0)
        {
            var fromDependencies = ExtractFromDependencies(dependencies);
            if (fromDependencies.Count > 0)
                return fromDependencies;
        }

        return ExtractFromEntityOrder(entities);
    }

    /// <summary>
    /// Maps dependency edges to entity-level relationships. Edges whose
    /// governor or dependent token didn't produce its own entity (e.g. a
    /// determiner or auxiliary attached intra-phrase) are skipped, since
    /// <see cref="Relationship"/> connects two entities, not two tokens.
    /// </summary>
    private static List<Relationship> ExtractFromDependencies(IReadOnlyList<Dependency> dependencies)
    {
        var relationships = new List<Relationship>();

        foreach (var dependency in dependencies)
        {
            var governorEntity = dependency.Governor.Entity;
            var dependentEntity = dependency.Dependent.Entity;

            if (governorEntity is null || dependentEntity is null || ReferenceEquals(governorEntity, dependentEntity))
                continue; // No entity on one side, or a self-loop (e.g. Root).

            var type = dependency.Type switch
            {
                DependencyType.Subject => RelationshipType.Subject,
                DependencyType.DirectObject => RelationshipTypeForEntity(dependentEntity, RelationshipType.Object),
                DependencyType.IndirectObject => RelationshipTypeForEntity(dependentEntity, RelationshipType.Object),
                DependencyType.Modifier => RelationshipTypeForEntity(dependentEntity, RelationshipType.Modifier),
                _ => (RelationshipType?)null
            };

            if (type is null)
                continue; // Predicate/Root/Complement/Unknown: not an entity-to-entity relationship.

            // Dependency edges point governor -> dependent (e.g. verb ->
            // subject, since the verb grammatically governs its subject).
            // Relationship keeps the opposite, "who does what" direction
            // for Subject specifically - Source = the subject, Target =
            // the verb it's the subject of - to match the convention the
            // positional fallback below already established (and that its
            // own unit tests assert on). Every other type already reads
            // the same direction as the raw edge (action -> object,
            // action -> time/location modifier).
            var (relSource, relTarget) = dependency.Type == DependencyType.Subject
                ? (dependentEntity, governorEntity)
                : (governorEntity, dependentEntity);

            relationships.Add(new Relationship
            {
                Source = relSource,
                Target = relTarget,
                Type = type.Value
            });
        }

        return relationships;
    }

    /// <summary>
    /// Refines a dependency edge's relationship type using what the
    /// dependent token's own entity classification already tells us -
    /// e.g. a Person becomes a Recipient, a Location stays a Location -
    /// regardless of whether the edge was a direct object, indirect
    /// object, or (date/time) modifier. Falls back to <paramref
    /// name="fallback"/> for entity types with no more specific
    /// relationship type of their own.
    /// </summary>
    private static RelationshipType RelationshipTypeForEntity(Entity target, RelationshipType fallback)
        => target.Type switch
        {
            EntityType.Person => RelationshipType.Recipient,
            EntityType.Location => RelationshipType.Location,
            EntityType.Date or EntityType.Time => RelationshipType.Time,
            _ => fallback
        };

    /// <summary>
    /// The original positional fallback: anchors on the first Action
    /// entity and classifies everything before it as the subject and
    /// everything after it as an object/recipient/time/location by entity
    /// type. See the class-level remarks for when this is used and its
    /// known limitations.
    /// </summary>
    private static List<Relationship> ExtractFromEntityOrder(IReadOnlyList<Entity> entities)
    {
        var relationships = new List<Relationship>();

        int actionIndex = -1;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].Type == EntityType.Action)
            {
                actionIndex = i;
                break;
            }
        }

        if (actionIndex < 0)
            return relationships;

        var action = entities[actionIndex];

        if (actionIndex > 0)
        {
            relationships.Add(new Relationship
            {
                Source = entities[actionIndex - 1],
                Target = action,
                Type = RelationshipType.Subject
            });
        }

        for (int i = actionIndex + 1; i < entities.Count; i++)
        {
            var type = entities[i].Type switch
            {
                EntityType.Person => RelationshipType.Recipient,
                EntityType.Date or EntityType.Time => RelationshipType.Time,
                EntityType.Location => RelationshipType.Location,
                _ => RelationshipType.Object
            };

            relationships.Add(new Relationship
            {
                Source = action,
                Target = entities[i],
                Type = type
            });
        }

        return relationships;
    }
}
