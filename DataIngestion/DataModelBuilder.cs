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

        var links = Links(tables);

        AddRelationships(model, links, sources);
        AddProposals(model, sources, links);
        AddEntityProposals(model, sources, links);
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

        /// <summary>The table's own key, holding values the app cannot number for itself (text
        /// codes, GUIDs), kept as a field under a name that does not read as the generated Id.</summary>
        KeptAsKeyField,

        /// <summary>A declared foreign key renamed to the <c>&lt;Name&gt;Id</c> spelling the
        /// generated app recognises a link by.</summary>
        RenamedForItsLink,

        /// <summary>A column whose name an earlier column already had (Notes beside Notes,
        /// e-mail beside Email), numbered so it keeps its own values.</summary>
        RenamedOffADuplicate,

        /// <summary>A column whose name starts with a digit, kept under a name a C# identifier
        /// can carry (<c>2ndRef</c> as <c>Ref2nd</c>).</summary>
        RenamedToAUsableName
    }

    /// <summary>The <see cref="object"/> members every class already declares. A field with one
    /// of these exact names hides it (CS0108) rather than failing to compile, so nothing else
    /// catches it before the generated project's build does.</summary>
    private static readonly HashSet<string> ReservedMemberNames =
        new(StringComparer.Ordinal) { "Equals", "ToString", "GetHashCode", "GetType" };

    private sealed record ColumnOutcome(
        string Column, string? Field, string Type, ColumnFate Fate, TypeFacts? Facts,
        string? DuplicateOf = null);

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
            .Select(c => WithIdSpelledId(NameNormalizer.ToFieldNameFromData(c.Name)))
            .ToList();

        // The table's own name, before it was made singular: Customers keyed by CustomersId is
        // as much keyed by itself as by CustomerId, and the keys pointing at it name that column.
        var tableKeyName = NameNormalizer.ToFieldName(table.Name);

        // A table read under another name (hr.Note as HrNote) is still keyed by NoteId.
        var sourceKeyNames = table.SourceName is { } sourceName
            ? new[] { NameNormalizer.ToUserTypedEntityName(sourceName), NameNormalizer.ToFieldName(sourceName) }
            : [];

        bool IsOwnKeyName(string name)
            => IsOwnKey(name, entityName) || IsOwnKey(name, tableKeyName)
               || sourceKeyNames.Any(source => IsOwnKey(name, source));

        // A declared key is spelled the way the generated app recognises a foreign key,
        // <Name>Id. Named for the TABLE rather than the class (TableDefinitionsId pointing at
        // TableDefinitions, whose class is TableDefinition), or not ending in Id at all
        // (RegionCode, Sku, TicketNo, a ReportsTo pointing at its own table), the class got a
        // navigation property nothing paired with the key, and the key stayed a plain value
        // that named rows the app had renumbered.
        var renamedForLink = new bool[columnNames.Count];

        for (var i = 0; i < columnNames.Count; i++)
        {
            foreach (var keyName in DeclaredLinkNames(table, table.Columns[i].Name, columnNames[i], entityName))
            {
                if (columnNames.Contains(keyName, StringComparer.OrdinalIgnoreCase) || IsOwnKeyName(keyName))
                    continue;

                columnNames[i] = keyName;
                renamedForLink[i] = true;
                break;
            }
        }

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

            var otherColumns = columnNames.Where((_, index) => index != i).ToList();

            // This entity's OWN key, by its name. A number is dropped, because DomainModelMapper
            // prepends `int Id` itself and numbers the rows 1..N, and the references to it are
            // renumbered to match. Anything else - a currency's GBP, a customer's C001, a GUID -
            // is the user's data and the app cannot regenerate it, so it is kept in a field named
            // so it does not read as the Id (a string Id compiles, then makes JsonStore throw on
            // the first Add). The references to it are still renumbered, the same way.
            if (!renamedForLink[i] && IsOwnKeyName(fieldName))
            {
                if (column.Type == "int"
                    || KeyFieldName(entityName, taken, otherColumns) is not { } keyField)
                {
                    outcomes.Add(new ColumnOutcome(
                        column.Name, null, column.Type, ColumnFate.DroppedGeneratedKey, facts));
                    continue;
                }

                taken.Add(keyField);
                outcomes.Add(new ColumnOutcome(
                    column.Name, keyField, column.Type, ColumnFate.KeptAsKeyField, facts));
                continue;
            }

            var resolved = Resolve(fieldName, entityName, taken, otherColumns);

            // An earlier column already has the name: "e-mail" and "Email", or "Order Date" and
            // "OrderDate", both want Email or OrderDate. Dropped, the second column's values were
            // lost without a record; numbered, it keeps them.
            if (resolved is null)
            {
                resolved = Numbered(fieldName, entityName, taken, otherColumns);
                taken.Add(resolved);
                outcomes.Add(new ColumnOutcome(
                    column.Name, resolved, column.Type, ColumnFate.RenamedOffADuplicate, facts,
                    column.DuplicateOf ?? column.Name));
                continue;
            }

            taken.Add(resolved);

            var fate = string.Equals(resolved, fieldName, StringComparison.Ordinal)
                ? renamedForLink[i] ? ColumnFate.RenamedForItsLink
                  : column.DuplicateOf is not null ? ColumnFate.RenamedOffADuplicate
                  : StartsWithADigit(column.Name) ? ColumnFate.RenamedToAUsableName
                  : ColumnFate.Kept
                : ReservedMemberNames.Contains(fieldName)
                    ? ColumnFate.RenamedOffAnInheritedMember
                    : ColumnFate.RenamedOffItsClassName;

            outcomes.Add(new ColumnOutcome(
                column.Name, resolved, column.Type, fate, facts, column.DuplicateOf));
        }

        return outcomes;
    }

    /// <summary>Whether <paramref name="column"/>'s first letter or digit is a digit, which
    /// <c>NameNormalizer.ToFieldNameFromData</c> moves behind the words so the name compiles.</summary>
    private static bool StartsWithADigit(string column)
        => column.FirstOrDefault(char.IsLetterOrDigit) is var first && char.IsDigit(first);

    /// <summary>
    /// The field a table's own non-numeric key is kept in: <c>&lt;Entity&gt;Code</c>, else
    /// <c>&lt;Entity&gt;Key</c>; null when the table already uses both names.
    /// </summary>
    private static string? KeyFieldName(
        string entityName, IReadOnlyList<string> taken, IReadOnlyList<string> otherColumns)
    {
        foreach (var candidate in new[] { entityName + "Code", entityName + "Key" })
        {
            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                && !otherColumns.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
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
    /// The column <paramref name="table"/> carried its own key in, whose values the references to
    /// it name — or null when it had none. The generated class numbers its rows 1..N whatever the
    /// key was, so every reference to one is renumbered through this column.
    /// </summary>
    /// <remarks>
    /// The key the source DECLARED, when it declared one: a ticket keyed by <c>TicketNo</c> is
    /// keyed by it, whatever it is called. Otherwise the key found by its name, dropped (a number)
    /// or kept in a field (anything else); <c>&lt;Entity&gt;Id</c> wins over a bare <c>Id</c> when
    /// a table has both, since that is the name another table's foreign key to this one is
    /// spelled with.
    /// </remarks>
    internal static string? KeyColumn(TabularTable table)
    {
        var declared = table.Columns.Where(c => c.IsKey).ToList();

        if (declared.Count == 1)
            return declared[0].Name;

        var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

        var keys = ResolveColumns(table, entityName)
            .Where(o => o.Fate is ColumnFate.DroppedGeneratedKey or ColumnFate.KeptAsKeyField)
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

                case ColumnFate.RenamedOffADuplicate:
                    var source = outcome.DuplicateOf ?? outcome.Column;
                    var evidence = $"'{source}' in {table.Label} became {entity.Name}.{outcome.Field}";

                    model.Record(source, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnRenamedOffADuplicate, evidence);

                    // Asked about by its new name too: `why Notes2`.
                    if (!string.Equals(source, outcome.Field, StringComparison.OrdinalIgnoreCase))
                        model.Record(outcome.Field!, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                            DecisionReasons.ColumnRenamedOffADuplicate, evidence);
                    break;

                case ColumnFate.KeptAsKeyField:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.KeyKeptAsAField,
                        $"'{outcome.Column}' in {table.Label} became {entity.Name}.{outcome.Field}; "
                        + $"each {entity.Name} is numbered 1, 2, 3... by its Id");
                    break;

                case ColumnFate.RenamedForItsLink:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnRenamedForItsLink,
                        $"'{outcome.Column}' in {table.Label} became {outcome.Field}");
                    break;

                case ColumnFate.RenamedOffItsClassName:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnWouldCollideWithItsClassName,
                        $"'{outcome.Column}' in {table.Label} became {outcome.Field}");
                    break;

                case ColumnFate.RenamedToAUsableName:
                    model.Record(outcome.Column, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                        DecisionReasons.ColumnRenamedToAUsableName,
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
    /// The names, best first, a column holding a declared foreign key should be given so the
    /// generated app sees it as one; empty when its own name already does.
    /// </summary>
    /// <remarks>
    /// Named after the target's TABLE (<c>TableDefinitionsId</c>): <c>&lt;Class&gt;Id</c>
    /// (<c>TableDefinitionId</c>). Not ending in Id at all (<c>RegionCode</c>, <c>Sku</c>,
    /// <c>TicketNo</c>): <c>&lt;Class&gt;Id</c>, else the column's own name plus Id, which keeps a
    /// second key to the same table apart. A reference to the entity's own table can only take
    /// its own name plus Id (<c>ReportsTo</c> → <c>ReportsToId</c>): <c>&lt;Class&gt;Id</c> would
    /// read as the entity's own key. A key whose name says more than its target
    /// (<c>BillToCustomerId</c>, <c>ManagerId</c>) is already spelled as a link and keeps its
    /// meaning.
    /// </remarks>
    private static IReadOnlyList<string> DeclaredLinkNames(
        TabularTable table, string column, string fieldName, string entityName)
    {
        var key = table.ForeignKeys.FirstOrDefault(
            k => string.Equals(k.Column, column, StringComparison.OrdinalIgnoreCase));

        if (key is null || fieldName.Length == 0)
            return [];

        var target = NameNormalizer.ToUserTypedEntityName(key.TargetTable);
        if (target.Length == 0)
            return [];

        var self = string.Equals(target, entityName, StringComparison.OrdinalIgnoreCase);
        var wanted = target + "Id";

        if (fieldName.EndsWith("Id", StringComparison.Ordinal) && fieldName.Length > 2)
        {
            return !self
                   && !string.Equals(fieldName, wanted, StringComparison.Ordinal)
                   && string.Equals(
                       NameNormalizer.ToUserTypedEntityName(fieldName[..^2]), target,
                       StringComparison.OrdinalIgnoreCase)
                ? [wanted]
                : [];
        }

        return self ? [fieldName + "Id"] : [wanted, fieldName + "Id"];
    }

    /// <summary>
    /// One link a table's rows hold to another table's rows (or to its own).
    /// </summary>
    /// <param name="Field">The field on the referencing entity that holds it.</param>
    /// <param name="Target">The entity it points at.</param>
    /// <param name="TargetColumn">
    /// The column of the target's table whose values the field's source values name, which is
    /// what they are renumbered through; null when they are already row numbers (a JSON child's,
    /// a normalization split's) or the target has no key to look them up in.
    /// </param>
    internal sealed record KeyLink(string Field, string Target, string? TargetColumn);

    /// <summary>
    /// Every link each entity's rows hold, by entity name: the foreign keys the source DECLARED,
    /// then the <c>&lt;Target&gt;Id</c> convention for the rest.
    /// </summary>
    /// <remarks>
    /// The single answer both <see cref="Build(IReadOnlyList{TabularTable})"/> (which makes each
    /// link a relationship) and <see cref="TabularImport"/> (which renumbers its values) read, so
    /// a relationship always has renumbered values and renumbered values always have a
    /// relationship. A declared key may point at its own table (a manager is a member of staff),
    /// and two may point at the same one (paid by, approved by): each is its own link.
    /// <para>
    /// The convention links a TEXT <c>&lt;Target&gt;Id</c> only to a table whose own key is text
    /// and holds at least one of its values: <c>Orders.CustomerId</c> "C002" to the customer
    /// keyed "C002". Anything else is left as the text it was read as.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyList<KeyLink>> Links(IReadOnlyList<TabularTable> tables)
    {
        var sources = Sources(tables);
        var links = new Dictionary<string, IReadOnlyList<KeyLink>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entity, table) in sources)
        {
            var fields = FieldNames(table);
            var found = new List<KeyLink>();

            bool Linked(string field) => found.Any(
                l => string.Equals(l.Field, field, StringComparison.OrdinalIgnoreCase));

            foreach (var key in table.ForeignKeys)
            {
                var target = Canonical(sources, NameNormalizer.ToUserTypedEntityName(key.TargetTable));

                if (target is null
                    || !fields.TryGetValue(key.Column, out var field)
                    || !IsLinkName(field)
                    || Linked(field))
                    continue;

                found.Add(new KeyLink(field, target, key.TargetColumn));
            }

            foreach (var column in table.Columns)
            {
                if (!fields.TryGetValue(column.Name, out var field) || !IsLinkName(field) || Linked(field))
                    continue;

                var target = Canonical(sources, field[..^2]);

                if (target is null || string.Equals(target, entity, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (column.HoldsRowNumbers)
                {
                    found.Add(new KeyLink(field, target, null));
                    continue;
                }

                var targetTable = sources[target];
                var keyColumn = KeyColumn(targetTable);

                if (column.Type == "int")
                {
                    found.Add(new KeyLink(field, target, keyColumn));
                    continue;
                }

                if (keyColumn is not null
                    && targetTable.Columns[targetTable.IndexOf(keyColumn)].Type != "int"
                    && NamesARow(table, column.Name, targetTable, keyColumn))
                    found.Add(new KeyLink(field, target, keyColumn));
            }

            links[entity] = found;
        }

        return links;
    }

    /// <summary>The entity each table becomes, under the same guards
    /// <see cref="Build(IReadOnlyList{TabularTable})"/> and <see cref="TabularImport"/> apply:
    /// a usable name, and the first table to claim it.</summary>
    internal static Dictionary<string, TabularTable> Sources(IReadOnlyList<TabularTable> tables)
    {
        var sources = new Dictionary<string, TabularTable>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

            if (entityName.Length == 0 || !NameNormalizer.IsValidIdentifier(entityName))
                continue;

            sources.TryAdd(entityName, table);
        }

        return sources;
    }

    /// <summary><paramref name="name"/> as the entity spells it, or null when no entity has it.</summary>
    private static string? Canonical(Dictionary<string, TabularTable> sources, string name)
        => name.Length == 0
            ? null
            : sources.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="field"/> is spelled the way the generated app recognises
    /// a link: <c>&lt;Name&gt;Id</c>.</summary>
    private static bool IsLinkName(string field)
        => field.Length > 2 && field.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>Whether any value of <paramref name="column"/> is a value of
    /// <paramref name="targetTable"/>'s <paramref name="keyColumn"/>.</summary>
    private static bool NamesARow(
        TabularTable table, string column, TabularTable targetTable, string keyColumn)
    {
        var keys = targetTable.ColumnValues(keyColumn)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToHashSet(StringComparer.Ordinal);

        return table.ColumnValues(column)
            .Any(v => !string.IsNullOrWhiteSpace(v) && keys.Contains(v.Trim()));
    }

    /// <summary>Whether <paramref name="fieldName"/> is this entity's own primary key —
    /// <c>Id</c>, or <c>&lt;Entity&gt;Id</c>. A <c>&lt;Other&gt;Id</c> is a foreign key and is
    /// kept.</summary>
    private static bool IsOwnKey(string fieldName, string entityName)
        => string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fieldName, entityName + "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="fieldName"/> numbered (Email2, Email3...) to the first form no column has
    /// taken or will want, and that is not the class's own name.
    /// </summary>
    private static string Numbered(
        string fieldName,
        string entityName,
        IReadOnlyList<string> takenNames,
        IEnumerable<string> otherColumns)
    {
        var reserved = new HashSet<string>(otherColumns, StringComparer.OrdinalIgnoreCase);

        for (var n = 2; ; n++)
        {
            var candidate = fieldName + n;

            if (!reserved.Contains(candidate)
                && !string.Equals(candidate, entityName, StringComparison.OrdinalIgnoreCase)
                && !takenNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }
    }

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
    /// One relationship per link <see cref="Links"/> found, each naming the field that holds it,
    /// and a text field whose values became links retyped to the number they now hold.
    /// </summary>
    /// <remarks>
    /// One per FIELD, not one per target: a payment paid by one member of staff and approved by
    /// another holds two links to Staff, and one per target collapsed them into a single
    /// navigation that neither key was paired with. A link to the entity's own table (a manager)
    /// is kept for the same reason - dropped, its values kept the source's keys while the rows
    /// they named were renumbered.
    /// </remarks>
    private static void AddRelationships(
        CandidateDomainModel model,
        IReadOnlyDictionary<string, IReadOnlyList<KeyLink>> links,
        IReadOnlyDictionary<string, TabularTable> sources)
    {
        foreach (var entity in model.Entities)
        {
            if (!links.TryGetValue(entity.Name, out var held))
                continue;

            foreach (var link in held)
            {
                var field = entity.Fields.FirstOrDefault(
                    f => string.Equals(f.Name, link.Field, StringComparison.OrdinalIgnoreCase));

                if (field is null)
                    continue;

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = link.Target,
                    Role = ReferencesRole,
                    ForeignKey = field.Name
                });

                if (field.Type == "int")
                    continue;

                // "EUR" becomes the Id of the currency keyed EUR. What the source declared about
                // the text (its length, its type) is not true of the number; whether it may be
                // missing still is.
                field.Type = "int";
                field.Facts = field.Facts is null
                    ? null
                    : new CodeGenerationEngine.Models.TypeFacts
                    {
                        IsNullable = field.Facts.IsNullable,
                        HasMissingValues = field.Facts.HasMissingValues,

                        // Renumbering keeps distinct values distinct, so a key the text was part
                        // of still holds of the numbers.
                        UniqueKeys = field.Facts.UniqueKeys
                    };

                var label = sources.TryGetValue(entity.Name, out var table) ? table.Label : entity.Name;

                model.Record(field.Name, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                    DecisionReasons.ValuesBecameLinks,
                    $"{entity.Name}.{field.Name} in {label} named each {link.Target} by its "
                    + $"{link.TargetColumn}; it now holds that {link.Target}'s Id");
            }
        }
    }

    /// <summary>The fields of <paramref name="entityName"/> that hold a link. Their values are
    /// the source's keys until <see cref="TabularImport"/> renumbers them, so no rule is read from
    /// them.</summary>
    private static HashSet<string> LinkFields(
        IReadOnlyDictionary<string, IReadOnlyList<KeyLink>> links, string entityName)
        => links.TryGetValue(entityName, out var held)
            ? held.Select(l => l.Field).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The rules each entity's own rows support, attached to the fields they are about.
    /// </summary>
    /// <remarks>
    /// After the relationship passes, because whether uniqueness may be proposed at all depends
    /// on them: an entity that references another is a transaction or a child, where a repeating
    /// value is the design rather than a defect. That is the first half of the same test
    /// <c>EntityRoles.IsTransaction</c> applies later over class definitions — stricter than it,
    /// deliberately, since the cost of not offering a rule is that the user types it themselves
    /// and the cost of offering a wrong one is an app that refuses good data. A link to the
    /// entity's own table (a manager) does not make it a child.
    /// <para>
    /// Runs over the COMPLETE tables. Everything downstream of <c>TabularImport</c> is capped at
    /// <c>ImportedData.MaxSeedRows</c>, and a rule inferred from a sample is the one thing this
    /// layer must never produce.
    /// </para>
    /// </remarks>
    private static void AddProposals(
        CandidateDomainModel model,
        IReadOnlyDictionary<string, TabularTable> sources,
        IReadOnlyDictionary<string, IReadOnlyList<KeyLink>> links)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            var mayBeUnique = entity.Relationships.All(
                r => string.Equals(r.Target, entity.Name, StringComparison.OrdinalIgnoreCase));

            var linkFields = LinkFields(links, entity.Name);

            foreach (var field in entity.Fields)
            {
                if (linkFields.Contains(field.Name))
                    continue;

                var column = ColumnFor(table, field.Name);

                if (column is null)
                    continue;

                var scan = ColumnScan.Of(table, column);

                // A column a CHECK limits to a list can never hold more distinct values than the
                // list has: three rows using all three statuses is not uniqueness, and accepting it
                // would refuse every new record.
                var fieldMayBeUnique = mayBeUnique && field.Facts?.AllowedValues is null;

                foreach (var proposal in RuleProposals.For(
                             scan, field.Name, field.Type, fieldMayBeUnique))
                    field.Proposals.Add(proposal);
            }
        }
    }

    /// <summary>The Cross-Field and Calculation rules each entity's own rows support, attached
    /// to the entity rather than one field. See <see cref="AddProposals"/> for why this runs
    /// over the complete tables.</summary>
    private static void AddEntityProposals(
        CandidateDomainModel model,
        IReadOnlyDictionary<string, TabularTable> sources,
        IReadOnlyDictionary<string, IReadOnlyList<KeyLink>> links)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            // A field spelled as a key (WarehouseId) holds a key even when it names no table of
            // this run: "WarehouseId is never more than Qty" is a fact about numbering, not a rule.
            var keyFields = LinkFields(links, entity.Name);
            keyFields.UnionWith(entity.Fields.Select(f => f.Name).Where(IsLinkName));

            foreach (var proposal in EntityRuleProposals.For(entity, table, keyFields))
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
