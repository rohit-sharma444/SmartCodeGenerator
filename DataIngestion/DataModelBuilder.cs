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

    private static void AddFields(
        CandidateDomainModel model, CandidateEntity entity, TabularTable table)
    {
        // Every other column's normalized name. A column that has to be renamed off its own
        // class's name must not be handed a name a LATER column already owns: that column has
        // not been added yet, so the added-fields check alone cannot see the collision coming.
        var columnNames = table.Columns
            .Select(c => NameNormalizer.ToFieldName(c.Name))
            .ToList();

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            var fieldName = columnNames[i];

            if (!NameNormalizer.IsValidIdentifier(fieldName))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.ColumnNameIsNotAValidIdentifier,
                    $"'{column.Name}' in {table.Label}");
                continue;
            }

            // This entity's OWN key. Dropped because DomainModelMapper prepends `int Id` itself,
            // and a read string Id compiles and then makes JsonStore throw on the first Add.
            if (IsOwnKey(fieldName, entity.Name))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.PrimaryKeyIsGenerated, $"'{column.Name}' in {table.Label}");
                continue;
            }

            var otherColumns = columnNames.Where((_, index) => index != i);
            var resolved = Resolve(fieldName, entity, otherColumns);
            if (resolved is null)
                continue;   // duplicate after normalization; the first spelling already won

            if (!string.Equals(resolved, fieldName, StringComparison.Ordinal))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                    DecisionReasons.ColumnWouldCollideWithItsClassName,
                    $"'{column.Name}' in {table.Label} became {resolved}");
            }

            entity.Fields.Add(new CandidateField
            {
                Name = resolved,
                Type = column.Type,
                Provenance = Provenance.Data
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
        string fieldName, CandidateEntity entity, IEnumerable<string> otherColumns)
    {
        bool Taken(string name) => entity.Fields.Any(
            f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        var collidesWithClass = string.Equals(
            fieldName, entity.Name, StringComparison.OrdinalIgnoreCase);

        if (!collidesWithClass)
            return Taken(fieldName) ? null : fieldName;

        var reserved = new HashSet<string>(otherColumns, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[] { entity.Name + NameSuffix, entity.Name + ValueSuffix })
        {
            if (!Taken(candidate) && !reserved.Contains(candidate))
                return candidate;
        }

        return null;
    }

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

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = target,
                    Role = ReferencesRole
                });
            }
        }
    }
}
