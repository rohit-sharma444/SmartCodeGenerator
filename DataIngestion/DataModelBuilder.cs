using DataIngestion.Schema;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion;

/// <summary>
/// Turns read tables into the same <see cref="CandidateDomainModel"/> Wikipedia discovery
/// produces, so the review loop and the scaffolder see one type and know nothing about where it
/// came from.
/// </summary>
/// <remarks>
/// There is no shaping stage here and there should not be. Shaping exists to decide whether a
/// noun harvested from prose is a domain class at all; a column in the user's own data needs no
/// such adjudication. What this class does instead is guard the two name collisions that make
/// the generated project fail to compile, and record every drop so `why` can explain it.
/// </remarks>
public static class DataModelBuilder
{
    /// <summary>First fallback for a column that shares its class's name.</summary>
    public const string NameSuffix = "Name";

    /// <summary>Second fallback, for when the first is taken as well.</summary>
    public const string ValueSuffix = "Value";

    /// <summary>The role every data-derived foreign key carries. Matches what
    /// <c>RelationshipMiner</c> emits, which is what <c>DomainModelMapper</c> reads.</summary>
    private const string ReferencesRole = "references";

    public static CandidateDomainModel Build(IReadOnlyList<TabularTable> tables)
        => Build(tables, []);

    /// <summary>
    /// As <see cref="Build(IReadOnlyList{TabularTable})"/>, additionally recording why each
    /// normalization split exists. <paramref name="applied"/> is what the user chose at the
    /// normalization prompt — the findings that were carried out, not everything found.
    /// </summary>
    public static CandidateDomainModel Build(
        IReadOnlyList<TabularTable> tables,
        IReadOnlyList<Normalization.NormalizationFinding> applied)
    {
        var model = new CandidateDomainModel();

        // Entity name -> the table it came from, so a foreign key can be resolved against the
        // set of entities this run actually produces rather than against a guess.
        var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

            if (entityName.Length == 0 || !NameNormalizer.IsValidIdentifier(entityName))
            {
                model.Record(table.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NameNormalizedToNothing, $"from {table.Label}");
                continue;
            }

            if (!entityNames.Add(entityName))
            {
                model.Record(table.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyNamed, $"{table.Label} would also become {entityName}");
                continue;
            }

            var entity = new CandidateEntity
            {
                Name = entityName,
                Provenance = Provenance.Data,
                Confidence = 1.0,   // the user's own data; nothing was guessed
            };

            AddFields(model, entity, table);

            // Every entity keeps at least one editable column, exactly as a hand-added one does.
            if (entity.Fields.Count == 0)
                DefaultFields.ApplyTo(entity, Provenance.Data);

            model.Entities.Add(entity);
            model.Record(entityName, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                DecisionReasons.ReadFromYourData,
                $"{table.Columns.Count} column(s) in {table.Label}");
        }

        // Declared first, so the convention pass below sees what is already there. These
        // are read from the constraint a database enforces; that one infers from a name.
        AddDeclaredRelationships(model, tables, entityNames);
        AddRelationships(model, entityNames);
        RecordNormalization(model, applied);
        return model;
    }

    /// <summary>
    /// One record per applied finding, filed under the name a user would ask about: the entity a
    /// split created, or the column a drop removed.
    /// </summary>
    private static void RecordNormalization(
        CandidateDomainModel model, IReadOnlyList<Normalization.NormalizationFinding> applied)
    {
        foreach (var finding in applied)
        {
            var evidence =
                $"{string.Join(", ", finding.Columns)} in {finding.TableName} "
                + $"({finding.SourceLabel})";

            switch (finding.Kind)
            {
                case Normalization.NormalizationFindingKind.RepeatingGroup:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromARepeatingGroup, evidence);
                    break;

                case Normalization.NormalizationFindingKind.NonAtomicValues:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromANonAtomicColumn, evidence);
                    break;

                case Normalization.NormalizationFindingKind.FunctionalDependency:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromAPartialDependency, evidence);
                    break;

                case Normalization.NormalizationFindingKind.DerivedColumn:
                    // A drop, not an extraction — and filed under the COLUMN, since that is the name
                    // a user would ask `why` about when it is missing from their model.
                    model.Record(finding.Columns[0], DecisionStage.Normalization,
                        DecisionOutcome.Rejected, DecisionReasons.DroppedAsADerivedColumn, evidence);
                    break;
            }
        }
    }

    /// <summary>Why a column did or did not become a field.</summary>
    private enum ColumnFate
    {
        Kept,
        RenamedOffItsClassName,
        DroppedUnusableName,
        DroppedGeneratedKey,
        DroppedDuplicate
    }

    private sealed record ColumnOutcome(string Column, string? Field, string Type, ColumnFate Fate);

    /// <summary>
    /// What each of <paramref name="table"/>'s columns becomes on an entity named
    /// <paramref name="entityName"/>: its final field name, or null if it is dropped, plus why.
    /// </summary>
    /// <remarks>
    /// The single source of truth for column-to-field naming. <see cref="AddFields"/> turns these
    /// into fields and decision records, and <see cref="FieldNames"/> exposes the same mapping to
    /// <see cref="TabularImport"/>. They MUST agree: a seed row keyed by the raw column name while
    /// the generated class declares the renamed one produces a class whose data silently never
    /// loads — exactly the defect the end-to-end test caught when these were computed twice.
    /// </remarks>
    private static List<ColumnOutcome> ResolveColumns(TabularTable table, string entityName)
    {
        var outcomes = new List<ColumnOutcome>();

        // Every other column's normalized name. A column that has to be renamed off its own
        // class's name must not be handed a name a LATER column already owns: that column has
        // not been resolved yet, so the taken-names check alone cannot see the collision coming.
        var columnNames = table.Columns
            .Select(c => NameNormalizer.ToFieldName(c.Name))
            .ToList();

        var taken = new List<string>();

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            var fieldName = columnNames[i];

            if (!NameNormalizer.IsValidIdentifier(fieldName))
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedUnusableName));
                continue;
            }

            // This entity's OWN key. Dropped because DomainModelMapper prepends `int Id` itself,
            // and a read string Id compiles and then makes JsonStore throw on the first Add.
            if (IsOwnKey(fieldName, entityName))
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedGeneratedKey));
                continue;
            }

            var otherColumns = columnNames.Where((_, index) => index != i);
            var resolved = Resolve(fieldName, entityName, taken, otherColumns);

            if (resolved is null)
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedDuplicate));
                continue;
            }

            taken.Add(resolved);

            outcomes.Add(new ColumnOutcome(
                column.Name, resolved, column.Type,
                string.Equals(resolved, fieldName, StringComparison.Ordinal)
                    ? ColumnFate.Kept
                    : ColumnFate.RenamedOffItsClassName));
        }

        return outcomes;
    }

    /// <summary>
    /// Each of <paramref name="table"/>'s surviving columns mapped to the field name the generated
    /// class will declare. Consumed by <see cref="TabularImport"/> so imported rows are keyed by the
    /// same names <see cref="Build(IReadOnlyList{TabularTable})"/> puts on the model.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FieldNames(TabularTable table)
    {
        var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var outcome in ResolveColumns(table, entityName))
            if (outcome.Field is not null)
                map[outcome.Column] = outcome.Field;

        return map;
    }

    private static void AddFields(
        CandidateDomainModel model, CandidateEntity entity, TabularTable table)
    {
        foreach (var outcome in ResolveColumns(table, entity.Name))
        {
            switch (outcome.Fate)
            {
                case ColumnFate.DroppedUnusableName:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction,
                        DecisionOutcome.Skipped, DecisionReasons.ColumnNameIsNotAValidIdentifier,
                        $"'{outcome.Column}' in {table.Label}");
                    continue;

                case ColumnFate.DroppedGeneratedKey:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction,
                        DecisionOutcome.Skipped, DecisionReasons.PrimaryKeyIsGenerated,
                        $"'{outcome.Column}' in {table.Label}");
                    continue;

                case ColumnFate.DroppedDuplicate:
                    continue;   // the first spelling already won

                case ColumnFate.RenamedOffItsClassName:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnWouldCollideWithItsClassName,
                        $"'{outcome.Column}' in {table.Label} became {outcome.Field}");
                    break;
            }

            entity.Fields.Add(new CandidateField
            {
                Name = outcome.Field!,
                Type = outcome.Type,
                Provenance = Provenance.Data,
                Facts = table.Columns.FirstOrDefault(c => c.Name == outcome.Column)?.Facts
            });
        }
    }

    /// <summary>Whether <paramref name="fieldName"/> is this entity's own primary key —
    /// <c>Id</c>, or <c>&lt;Entity&gt;Id</c>. A <c>&lt;Other&gt;Id</c> is a foreign key and is
    /// kept.</summary>
    private static bool IsOwnKey(string fieldName, string entityName)
        => string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fieldName, entityName + "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name this field can actually carry: itself, or a suffixed form when it would collide
    /// with its own class (CS0542). Null when every candidate is already taken, in which case the
    /// column is a duplicate of one already added.
    /// </summary>
    /// <param name="otherColumns">
    /// The normalized names of the table's other columns. Consulted only when picking a
    /// replacement name, so a rename never steals a name another column is going to want, while
    /// two columns that normalize to the SAME name still resolve the plain way — first wins.
    /// </param>
    private static string? Resolve(
        string fieldName,
        string entityName,
        IReadOnlyList<string> takenNames,
        IEnumerable<string> otherColumns)
    {
        bool Taken(string name) => takenNames.Any(
            n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        var collidesWithClass = string.Equals(
            fieldName, entityName, StringComparison.OrdinalIgnoreCase);

        if (!collidesWithClass)
            return Taken(fieldName) ? null : fieldName;

        var reserved = new HashSet<string>(otherColumns, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[] { entityName + NameSuffix, entityName + ValueSuffix })
        {
            if (!Taken(candidate) && !reserved.Contains(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Turns the foreign keys a source DECLARED into relationships, before the
    /// <c>&lt;Target&gt;Id</c> convention gets a look at the same columns.
    /// </summary>
    /// <remarks>
    /// A foreign key's target is a RAW table name, and entity names have been through
    /// <see cref="NameNormalizer.ToUserTypedEntityName"/> - so it is normalized the same way
    /// before being matched. Comparing the two directly matches nothing, and loses every
    /// relationship without a word.
    /// <para>
    /// Same two refusals as <see cref="AddRelationships"/>, for the same reasons: a target
    /// that is not an entity this run produced would become a navigation property typed as a
    /// class that is never generated, and a self-reference has no second class to point at.
    /// </para>
    /// </remarks>
    private static void AddDeclaredRelationships(
        CandidateDomainModel model,
        IReadOnlyList<TabularTable> tables,
        HashSet<string> entityNames)
    {
        foreach (var table in tables)
        {
            if (table.ForeignKeys.Count == 0)
                continue;

            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

            var entity = model.Entities.FirstOrDefault(
                e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
                continue;

            foreach (var key in table.ForeignKeys)
            {
                var target = NameNormalizer.ToUserTypedEntityName(key.TargetTable);

                if (target.Length == 0
                    || !entityNames.Contains(target)
                    || string.Equals(target, entity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (HasRelationshipTo(entity, target))
                    continue;

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = target,
                    Role = ReferencesRole
                });
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="entity"/> already references <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// What stops a declared key and the convention emitting the same relationship twice.
    /// Twice would have <c>DomainModelMapper.AddNavigationProperties</c> write one navigation
    /// property twice, and a SQL foreign key on a column called <c>CustomerId</c> is seen by
    /// both passes by construction.
    /// </remarks>
    private static bool HasRelationshipTo(CandidateEntity entity, string target)
        => entity.Relationships.Any(
            r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pairs each <c>&lt;Target&gt;Id</c> field with a relationship to that target, but only when
    /// the target is an entity this run produced. Without that check a column named
    /// <c>LegacyId</c> would emit a navigation property typed as a class that is never generated,
    /// which does not compile — the same trap <c>DomainModelMapper</c> guards on its own side.
    /// </summary>
    private static void AddRelationships(
        CandidateDomainModel model, HashSet<string> entityNames)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.Type != "int"
                    || !field.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = field.Name[..^2];

                if (target.Length == 0
                    || !entityNames.Contains(target)
                    || string.Equals(target, entity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (HasRelationshipTo(entity, target))
                    continue;

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = target,
                    Role = ReferencesRole
                });
            }
        }
    }
}
