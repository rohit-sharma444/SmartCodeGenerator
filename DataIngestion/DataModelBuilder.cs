using AppScaffolding;
using CodeGenerationEngine.Models;
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
/// such adjudication. What this class does instead is guard the name collisions that make the
/// generated project fail to build -- either outright, or under -warnaserror -- and record every
/// drop so `why` can explain it.
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

        // Entity name -> the table it came from, so proposals can be scanned from the complete
        // rows after relationships are known.
        var sources = new Dictionary<string, TabularTable>(StringComparer.OrdinalIgnoreCase);

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

                // The key TabularImport files this table's rows under.
                ImportedAs = entityName,
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

            sources[entityName] = table;
        }

        // Declared first, so the convention pass below sees what is already there. These
        // are read from the constraint a database enforces; that one infers from a name.
        AddDeclaredRelationships(model, tables, entityNames);
        AddRelationships(model, entityNames);
        AddProposals(model, sources);
        AddEntityProposals(model, sources);
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

    /// <summary>
    /// One line for each text column the generated class will drop because the same row links to
    /// the table it is named for: Shipment's <c>Customer</c> text beside a <c>CustomerId</c> key to
    /// Customer.
    /// </summary>
    /// <remarks>
    /// The drop itself is <c>DomainModelMapper.DropTextShadowingTheRelationship</c>, and this asks
    /// its question of the model: a <c>string</c> field named for a relationship's target, and an
    /// int <c>&lt;Target&gt;Id</c> field on the same entity. The class keeps the link, and the
    /// text's values were never seeded anywhere — said here, or they vanish without a word.
    /// </remarks>
    public static IReadOnlyList<string> ReplacedTextNotes(CandidateDomainModel model)
    {
        var entityNames = new HashSet<string>(
            model.Entities.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

        var notes = new List<string>();

        foreach (var entity in model.Entities)
        foreach (var target in entity.Relationships.Select(r => r.Target).Distinct(StringComparer.Ordinal))
        {
            if (!entityNames.Contains(target))
                continue;

            var text = entity.Fields.Any(
                f => string.Equals(f.Name, target, StringComparison.Ordinal) && f.Type == "string");
            var key = entity.Fields.Any(
                f => string.Equals(f.Name, target + "Id", StringComparison.Ordinal)
                     && DomainModelMapper.BaseType(f.Type) == "int");

            if (text && key)
                notes.Add($"{entity.Name}.{target} (text) was replaced by the link to {target}; "
                          + "its values were not kept.");
        }

        return notes;
    }

    /// <summary>Why a column did or did not become a field.</summary>
    private enum ColumnFate
    {
        Kept,
        RenamedOffItsClassName,
        RenamedOffAnInheritedMember,
        DroppedUnusableName,
        DroppedGeneratedKey,
        DroppedDuplicate
    }

    /// <summary>The <see cref="object"/> members every class already declares. A field with one
    /// of these exact names hides it (CS0108) rather than failing to compile, so nothing else
    /// catches it before the generated project's build does.</summary>
    private static readonly HashSet<string> ReservedMemberNames =
        new(StringComparer.Ordinal) { "Equals", "ToString", "GetHashCode", "GetType" };

    private sealed record ColumnOutcome(
        string Column, string? Field, string Type, ColumnFate Fate, TypeFacts? Facts);

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
            .Select(c => WithIdSpelledId(NameNormalizer.ToFieldName(c.Name)))
            .ToList();

        // A declared key named for the TABLE rather than the class — TableDefinitionsId pointing
        // at TableDefinitions, whose class is TableDefinition — is spelled the way the generated
        // app recognises a foreign key, <Class>Id. Left as it was, the class gets a navigation
        // property nothing pairs with the key, and the key is just an int.
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (DeclaredClassKeyName(table, table.Columns[i].Name, columnNames[i], entityName) is not { } keyName)
                continue;

            if (!columnNames.Contains(keyName, StringComparer.OrdinalIgnoreCase))
                columnNames[i] = keyName;
        }

        // The table's own name, before it was made singular: Customers keyed by CustomersId is
        // as much keyed by itself as by CustomerId, and the keys pointing at it name that column.
        var tableKeyName = NameNormalizer.ToFieldName(table.Name);

        var taken = new List<string>();

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            var fieldName = columnNames[i];
            var facts = EffectiveFacts(column, fieldName, table);

            if (!NameNormalizer.IsValidIdentifier(fieldName))
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedUnusableName, facts));
                continue;
            }

            // This entity's OWN key. Dropped because DomainModelMapper prepends `int Id` itself,
            // and a read string Id compiles and then makes JsonStore throw on the first Add.
            if (IsOwnKey(fieldName, entityName) || IsOwnKey(fieldName, tableKeyName))
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedGeneratedKey, facts));
                continue;
            }

            var otherColumns = columnNames.Where((_, index) => index != i);
            var resolved = Resolve(fieldName, entityName, taken, otherColumns);

            if (resolved is null)
            {
                outcomes.Add(new ColumnOutcome(
                    column.Name, null, column.Type, ColumnFate.DroppedDuplicate, facts));
                continue;
            }

            taken.Add(resolved);

            var fate = string.Equals(resolved, fieldName, StringComparison.Ordinal)
                ? ColumnFate.Kept
                : ReservedMemberNames.Contains(fieldName)
                    ? ColumnFate.RenamedOffAnInheritedMember
                    : ColumnFate.RenamedOffItsClassName;

            outcomes.Add(new ColumnOutcome(column.Name, resolved, column.Type, fate, facts));
        }

        return outcomes;
    }

    /// <summary>
    /// <paramref name="column"/>'s own facts, checked against what it actually holds before any
    /// rule gets to assume them: a NOT NULL column's own blank value withdraws the "required" rule
    /// that fact would otherwise add (NOT NULL forbids a missing value, not an empty one); a
    /// string with NO facts at all has its longest actual value replace the generic 200-character
    /// default when that value is already longer; and a column named for the email/phone format
    /// the generator would otherwise apply by name alone is checked against every value it holds.
    /// </summary>
    private static TypeFacts? EffectiveFacts(TabularColumn column, string fieldName, TabularTable table)
    {
        var values = table.ColumnValues(column.Name).ToList();

        if (column.Type != "string")
            return WithMissingValues(column.Facts, values);

        return WithMissingValues(StringFacts(column, fieldName, values), values);
    }

    /// <summary>
    /// <paramref name="facts"/> with <see cref="TypeFacts.HasMissingValues"/> set when any row is
    /// blank. Recorded for every column, string or not, so a column the user retypes in the review
    /// loop from string to a number still comes out nullable when its rows have gaps.
    /// </summary>
    private static TypeFacts? WithMissingValues(TypeFacts? facts, List<string?> values)
    {
        if (!values.Any(string.IsNullOrWhiteSpace))
            return facts;

        return (facts ?? new TypeFacts()) with { HasMissingValues = true };
    }

    private static TypeFacts? StringFacts(TabularColumn column, string fieldName, List<string?> values)
    {
        if (column.Facts is not null)
        {
            var requiredHolds = column.Facts.IsNullable != false
                || values.All(v => !string.IsNullOrWhiteSpace(v));

            return requiredHolds ? column.Facts : column.Facts with { IsNullable = null };
        }

        var longest = values.Count == 0 ? 0 : values.Max(v => v?.Length ?? 0);

        bool? formatHolds = DomainModelMapper.NameContainsWord(fieldName, "email")
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).All(v => EmailFormat.IsValid(v))
            : DomainModelMapper.NameContainsWord(fieldName, "phone")
                ? values.Where(v => !string.IsNullOrWhiteSpace(v)).All(v => PhoneFormat.IsValid(v))
                : null;

        if (longest <= DomainModelMapper.DefaultStringLength && formatHolds is not false)
            return null;

        return new TypeFacts
        {
            MaxLength = longest > DomainModelMapper.DefaultStringLength ? longest : null,
            NameImpliedFormatHolds = formatHolds
        };
    }

    private static readonly System.ComponentModel.DataAnnotations.EmailAddressAttribute EmailFormat = new();
    private static readonly System.ComponentModel.DataAnnotations.PhoneAttribute PhoneFormat = new();

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

    /// <summary>
    /// The column <paramref name="table"/> carried its own key in — the one dropped because the
    /// generated class numbers its rows itself — or null when it had none. <c>&lt;Entity&gt;Id</c>
    /// wins over a bare <c>Id</c> when a table has both, since that is the name another table's
    /// foreign key to this one is spelled with.
    /// </summary>
    internal static string? KeyColumn(TabularTable table)
    {
        var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

        var keys = ResolveColumns(table, entityName)
            .Where(o => o.Fate == ColumnFate.DroppedGeneratedKey)
            .Select(o => o.Column)
            .ToList();

        return keys.FirstOrDefault(k => !string.Equals(
                   NameNormalizer.ToFieldName(k), "Id", StringComparison.OrdinalIgnoreCase))
               ?? keys.FirstOrDefault();
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

                case ColumnFate.RenamedOffAnInheritedMember:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnWouldHideAnInheritedMember,
                        $"'{outcome.Column}' in {table.Label} became {outcome.Field}");
                    break;
            }

            entity.Fields.Add(new CandidateField
            {
                Name = outcome.Field!,

                // The key TabularImport files this column's values under: the same resolution.
                ImportedAs = outcome.Field!,
                Type = outcome.Type,
                Provenance = Provenance.Data,
                Facts = outcome.Facts
            });
        }
    }

    /// <summary>
    /// <c>CustomerID</c> as <c>CustomerId</c>. The generated app recognises a foreign key only
    /// by the exact <c>&lt;Class&gt;Id</c> spelling — its seeder, validators and lookup pickers
    /// all match it case for case — so a key spelled the SQL Server way got a navigation
    /// property and was otherwise never treated as one. An all-capitals name (<c>GUID</c>,
    /// <c>SSID</c>) is an acronym, not a key suffix, and is left alone.
    /// </summary>
    private static string WithIdSpelledId(string fieldName)
        => fieldName.Length > 2
           && fieldName.EndsWith("ID", StringComparison.Ordinal)
           && !char.IsUpper(fieldName[^3])
            ? fieldName[..^2] + "Id"
            : fieldName;

    /// <summary>
    /// <c>&lt;Class&gt;Id</c> for a column that holds a declared foreign key and is named after the
    /// target's table (<c>TableDefinitionsId</c> → <c>TableDefinitionId</c>); null for any other
    /// column, for a key already spelled that way, for a self-reference (whose
    /// <c>&lt;Class&gt;Id</c> would read as the entity's own key and be dropped), and for a key
    /// whose name says more than its target (<c>BillToCustomerId</c> keeps its meaning).
    /// </summary>
    private static string? DeclaredClassKeyName(
        TabularTable table, string column, string fieldName, string entityName)
    {
        var key = table.ForeignKeys.FirstOrDefault(
            k => string.Equals(k.Column, column, StringComparison.OrdinalIgnoreCase));

        if (key is null || !fieldName.EndsWith("Id", StringComparison.Ordinal) || fieldName.Length <= 2)
            return null;

        var target = NameNormalizer.ToUserTypedEntityName(key.TargetTable);
        var wanted = target + "Id";

        if (target.Length == 0
            || string.Equals(target, entityName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fieldName, wanted, StringComparison.Ordinal)
            || !string.Equals(
                NameNormalizer.ToUserTypedEntityName(fieldName[..^2]), target, StringComparison.OrdinalIgnoreCase))
            return null;

        return wanted;
    }

    /// <summary>The entity each of <paramref name="table"/>'s DECLARED foreign-key fields points
    /// at, by field name. Self-references are left out.</summary>
    internal static IReadOnlyDictionary<string, string> DeclaredKeyTargets(TabularTable table)
    {
        var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);
        var fields = FieldNames(table);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in table.ForeignKeys)
        {
            var target = NameNormalizer.ToUserTypedEntityName(key.TargetTable);

            if (target.Length == 0
                || string.Equals(target, entityName, StringComparison.OrdinalIgnoreCase)
                || !fields.TryGetValue(key.Column, out var field))
                continue;

            map.TryAdd(field, target);
        }

        return map;
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

        var needsRename = string.Equals(fieldName, entityName, StringComparison.OrdinalIgnoreCase)
            || ReservedMemberNames.Contains(fieldName);

        if (!needsRename)
            return Taken(fieldName) ? null : fieldName;

        var reserved = new HashSet<string>(otherColumns, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[] { fieldName + NameSuffix, fieldName + ValueSuffix })
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

    /// <summary>
    /// The rules each entity's own rows support, attached to the fields they are about.
    /// </summary>
    /// <remarks>
    /// After the relationship passes, because whether uniqueness may be proposed at all depends
    /// on them: an entity that references another is a transaction or a child, where a repeating
    /// value is the design rather than a defect. That is the first half of the same test
    /// <c>EntityRoles.IsTransaction</c> applies later over class definitions — stricter than it,
    /// deliberately, since the cost of not offering a rule is that the user types it themselves
    /// and the cost of offering a wrong one is an app that refuses good data.
    /// <para>
    /// Runs over the COMPLETE tables. Everything downstream of <c>TabularImport</c> is capped at
    /// <c>ImportedData.MaxSeedRows</c>, and a rule inferred from a sample is the one thing this
    /// layer must never produce.
    /// </para>
    /// </remarks>
    private static void AddProposals(
        CandidateDomainModel model, IReadOnlyDictionary<string, TabularTable> sources)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            var mayBeUnique = entity.Relationships.Count == 0;

            foreach (var field in entity.Fields)
            {
                var column = ColumnFor(table, field.Name);

                if (column is null)
                    continue;

                var scan = ColumnScan.Of(table, column);

                foreach (var proposal in RuleProposals.For(
                             scan, field.Name, field.Type, mayBeUnique))
                    field.Proposals.Add(proposal);
            }
        }
    }

    /// <summary>The Cross-Field and Calculation rules each entity's own rows support, attached
    /// to the entity rather than one field. See <see cref="AddProposals"/> for why this runs
    /// over the complete tables.</summary>
    private static void AddEntityProposals(
        CandidateDomainModel model, IReadOnlyDictionary<string, TabularTable> sources)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            foreach (var proposal in EntityRuleProposals.For(entity, table))
                entity.Proposals.Add(proposal);
        }
    }

    /// <summary>
    /// The source column that became <paramref name="fieldName"/> on the entity built from
    /// <paramref name="table"/>, or null when no column produced it (a default field, or one
    /// added after the fact). The inverse of <see cref="FieldNames"/> — a field name may have
    /// been renamed off its class name by <see cref="ResolveColumns"/>, and this looks up the
    /// same recorded pair rather than guessing at one.
    /// </summary>
    internal static string? ColumnFor(TabularTable table, string fieldName)
    {
        foreach (var (column, field) in FieldNames(table))
            if (string.Equals(field, fieldName, StringComparison.OrdinalIgnoreCase))
                return column;

        return null;
    }
}
