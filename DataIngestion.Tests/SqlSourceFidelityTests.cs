using System.Reflection;
using System.Text.Json;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;
using DataIngestion.Tests.Schema.Readers.Sql;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// What a SQL Server table declares beyond its column types — lengths, nullability, offsets,
/// constraints, defaults, computed columns — and whether the generated app keeps it.
/// </summary>
/// <remarks>
/// Read through <see cref="SqlServerReader"/> over a fake database, so each declaration arrives
/// exactly as the real reader hands it on; the generated models and validators are compiled and
/// run, never matched as text.
/// </remarks>
public class SqlSourceFidelityTests
{
    private const string Connection = "Server=.";

    private static FakeSqlSchemaSource Database(Action<Func<string, SqlColumnInfo[], string?[][], string>> tables)
    {
        var source = new FakeSqlSchemaSource();

        tables((name, columns, rows) =>
        {
            source.TableList.Add(new SqlTableInfo("dbo", name, columns.Length, rows.Length));
            source.ColumnsByTable[name] = [.. columns];
            source.RowsByTable[name] = [.. rows];
            return name;
        });

        return source;
    }

    /// <summary>One table called <paramref name="name"/>, read.</summary>
    private static TabularTable ReadOne(string name, SqlColumnInfo[] columns, params string?[][] rows)
        => Read(Database(add => add(name, columns, rows))).Single();

    private static IReadOnlyList<TabularTable> Read(FakeSqlSchemaSource source)
        => new SqlServerReader(source).Read(new DataSourceSpec(DataSourceKind.Sql, Connection, "Db", []));

    private sealed record Generated(
        IReadOnlyList<TabularTable> Tables,
        CandidateDomainModel Model,
        IReadOnlyList<ClassDefinition> Classes,
        Dictionary<string, string> SeedJson,
        IReadOnlyList<string> Unreadable,
        ImportedData Data);

    private static Generated Generate(
        FakeSqlSchemaSource source, Action<CandidateDomainModel>? review = null, bool normalize = false)
    {
        var tables = Read(source);
        if (normalize)
            tables = DataIngestion.Normalization.NormalizationPlan.Apply(tables,
                [.. tables.SelectMany(t => DataIngestion.Normalization.NormalizationAnalyzer.Analyze(t).Findings)]);

        return Generate(tables, review);
    }

    /// <summary>As <see cref="Generate(FakeSqlSchemaSource, Action{CandidateDomainModel}?, bool)"/>,
    /// from tables any reader produced.</summary>
    private static Generated Generate(IReadOnlyList<TabularTable> tables, Action<CandidateDomainModel>? review = null)
    {
        var model = DataModelBuilder.Build(tables);
        review?.Invoke(model);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
        var data = TabularImport.ToImportedData(tables);

        var seeds = SeedFileWriter.Files(classes, classNames, data, out var unreadable)
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.FileName), f => f.Json);

        return new Generated(tables, model, classes, seeds, unreadable, data);
    }

    private static JsonElement[] Seed(Generated app, string className)
        => JsonDocument.Parse(app.SeedJson[className]).RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();

    private static ClassDefinition Class(Generated app, string name) => app.Classes.Single(c => c.Name == name);

    private static PropertyDefinition Property(Generated app, string className, string name)
        => Class(app, className).Properties.Single(p => p.Name == name);

    /// <summary>
    /// The generated models and validators, compiled. Fails the test with the compiler's own
    /// errors when they do not compile.
    /// </summary>
    private static Assembly Compile(Generated app)
    {
        var names = ForeignKeys.ClassNameSet(app.Classes);

        var files = new List<(string Path, string Code)>
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", Lookup),
            ("Validation/ListLookup.cs", ListLookup),
        };

        foreach (var definition in app.Classes)
        {
            files.Add(($"Models/{definition.Name}.cs", RoslynClassGenerator.Generate(definition, "Acme.Models")));
            files.Add(($"Validation/{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.GeneratedForLinks("Acme", definition, ForeignKeys.LinksOf(definition, names),
                    referencesRequired: false,
                    referencedBy: ForeignKeys.ReferencesTo(definition.Name, app.Classes),
                    businessRules: definition.BusinessRules)));
            files.Add(($"Validation/{definition.Name}Validator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)));
        }

        var result = Compiler.CompileSources(files);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        return Assembly.Load(result.AssemblyBytes!);
    }

    /// <summary>What <paramref name="className"/>'s validator says of <paramref name="item"/>.</summary>
    private static IReadOnlyList<string> Validate(Assembly assembly, string className, object item, string operation = "Insert")
        => ValidateWith(assembly, className, item,
            Activator.CreateInstance(assembly.GetType("Acme.Validation.StoreEntityLookup")!)!, operation);

    /// <summary>As <see cref="Validate"/>, over <paramref name="lookup"/>.</summary>
    private static IReadOnlyList<string> ValidateWith(
        Assembly assembly, string className, object item, object lookup, string operation = "Insert")
    {
        var type = assembly.GetType("Acme.Models." + className)!;
        var validatorType = assembly.GetType($"Acme.Validation.{className}Validator")!;
        var context = assembly.GetType("Acme.Validation.ValidationContext`1")!.MakeGenericType(type)
            .GetMethod("For")!
            .Invoke(null, [item, Enum.Parse(assembly.GetType("Acme.Validation.ValidationOperation")!, operation), lookup]);
        var outcome = validatorType.GetMethod("Validate")!.Invoke(Activator.CreateInstance(validatorType), [context])!;
        return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
    }

    /// <summary>A new <paramref name="className"/> with <paramref name="values"/> set.</summary>
    private static object New(Assembly assembly, string className, params (string Property, object? Value)[] values)
    {
        var type = assembly.GetType("Acme.Models." + className)!;
        var item = Activator.CreateInstance(type)!;
        foreach (var (property, value) in values)
            type.GetProperty(property)!.SetValue(item, value);
        return item;
    }

    /// <summary>Every seed row of <paramref name="className"/>, loaded as the generated app loads
    /// it.</summary>
    private static IReadOnlyList<object> SeedRows(Assembly assembly, Generated app, string className)
    {
        var type = assembly.GetType("Acme.Models." + className)!;
        var listType = typeof(List<>).MakeGenericType(type);
        var list = (System.Collections.IEnumerable)JsonSerializer.Deserialize(app.SeedJson[className], listType)!;
        return [.. list.Cast<object>()];
    }

    /// <summary>Ids 1..9 exist; nothing references anything; no value is taken.</summary>
    private const string Lookup = """
        namespace Acme.Validation;

        public sealed class StoreEntityLookup : IEntityLookup
        {
            public bool Exists<TEntity>(int id) where TEntity : class, new() => id is >= 1 and <= 9;
            public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new() => 0;
            public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new() => false;
        }
        """;

    /// <summary>Ids 1..9 exist; the records given are the ones stored, compared by reflection.</summary>
    private const string ListLookup = """
        using System.Linq;

        namespace Acme.Validation;

        public sealed class ListLookup(object[] stored) : IEntityLookup
        {
            public bool Exists<TEntity>(int id) where TEntity : class, new() => id is >= 1 and <= 9;
            public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new() => 0;

            public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new()
                => AnyOtherWithAll<TEntity>([propertyName], [value], exceptId);

            public bool AnyOtherWithAll<TEntity>(string[] propertyNames, object?[] values, int exceptId) where TEntity : class, new()
                => stored.OfType<TEntity>().Any(item =>
                    !Equals(typeof(TEntity).GetProperty("Id")!.GetValue(item), exceptId)
                    && propertyNames.Select((name, i) => Equals(typeof(TEntity).GetProperty(name)!.GetValue(item), values[i])).All(same => same));
        }
        """;

    // --- S1, S5, S14: the lengths a column really allows ---

    [Fact]
    public void Text_ntext_and_image_are_unbounded_and_their_validator_compiles()
    {
        var table = ReadOne("Legacy",
            [
                new("LegacyId", "int", false, IsPrimaryKey: true),
                new("LegacyNotes", "text", true, 2147483647),
                new("Memo", "ntext", true, 1073741823),
                new("Picture", "image", true, 2147483647),
            ],
            ["1", "old", "older", "AQID"]);

        foreach (var name in new[] { "LegacyNotes", "Memo", "Picture" })
        {
            var facts = table.Columns.Single(c => c.Name == name).Facts!;
            Assert.True(facts.IsUnbounded, name);
            Assert.Null(facts.MaxLength);
        }

        var app = Generate(Database(add => add("Legacy",
            [new("LegacyId", "int", false, IsPrimaryKey: true), new("LegacyNotes", "text", true, 2147483647)],
            [["1", "old"]])));

        Assert.DoesNotContain(Property(app, "Legacy", "LegacyNotes").Rules, r => r.Name == "maxlength");
        Compile(app);
    }

    [Fact]
    public void A_length_bound_at_int_MaxValue_emits_no_impossible_check()
    {
        // Whatever reaches it, the template must not write `Length: > 2147483647` (CS8518).
        var definition = new ClassDefinition { Name = "Note" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition
        {
            Name = "Body", Type = "string", Rules = [new PropertyRule { Name = "maxlength", MaxLength = int.MaxValue }]
        });

        var source = ValidatorTemplate.GeneratedForLinks("Acme", definition, [], referencesRequired: false);

        Assert.DoesNotContain("2147483647", source);
    }

    [Fact]
    public void A_binary_column_is_bounded_by_the_length_of_its_base64_text()
    {
        var table = ReadOne("Doc",
            [
                new("DocId", "int", false, IsPrimaryKey: true),
                new("Hash", "varbinary", false, 16),
                new("Flags", "binary", false, 3),
                new("Blob", "varbinary", true, -1),
            ],
            ["1", "ABEiM0RVZneImaq7zN3u/w==", "AQID", null]);

        Assert.Equal(24, table.Columns.Single(c => c.Name == "Hash").Facts!.MaxLength);
        Assert.Equal(4, table.Columns.Single(c => c.Name == "Flags").Facts!.MaxLength);
        Assert.True(table.Columns.Single(c => c.Name == "Blob").Facts!.IsUnbounded);

        var app = Generate(Database(add => add("Doc",
            [new("DocId", "int", false, IsPrimaryKey: true), new("Hash", "varbinary", false, 16), new("Title", "nvarchar", false, 20)],
            [["1", "ABEiM0RVZneImaq7zN3u/w==", "Spec"]])));

        var assembly = Compile(app);
        foreach (var row in SeedRows(assembly, app, "Doc"))
            Assert.Empty(Validate(assembly, "Doc", row));
    }

    [Fact]
    public void Time_and_uniqueidentifier_are_bounded_by_their_text_not_an_invented_200()
    {
        var table = ReadOne("Shift",
            [
                new("ShiftId", "int", false, IsPrimaryKey: true),
                new("ShiftStart", "time", false),
                new("Token", "uniqueidentifier", false),
            ],
            ["1", "23:59:59", "5f2b4e1c-0c3a-4d1e-9a51-6f7f3d2b8e10"]);

        Assert.Equal(16, table.Columns.Single(c => c.Name == "ShiftStart").Facts!.MaxLength);
        Assert.Equal(36, table.Columns.Single(c => c.Name == "Token").Facts!.MaxLength);
    }

    // --- S16: money's real range ---

    [Fact]
    public void Money_and_smallmoney_are_bounded_by_what_they_really_hold()
    {
        var app = Generate(Database(add => add("Fee",
            [
                new("FeeId", "int", false, IsPrimaryKey: true),
                new("Amount", "money", false, Precision: 19, Scale: 4),
                new("Small", "smallmoney", false, Precision: 10, Scale: 4),
            ],
            [["1", "922337203685477.5807", "-214748.3648"]])));

        decimal? Bound(string property, string rule)
        {
            var found = Property(app, "Fee", property).Rules.Single(r => r.Name == rule);
            return rule == "minvalue" ? found.MinValue : found.MaxValue;
        }

        Assert.Equal(922337203685477.5807m, Bound("Amount", "maxvalue"));
        Assert.Equal(-922337203685477.5808m, Bound("Amount", "minvalue"));
        Assert.Equal(214748.3647m, Bound("Small", "maxvalue"));
        Assert.Equal(-214748.3648m, Bound("Small", "minvalue"));

        var assembly = Compile(app);
        Assert.Empty(Validate(assembly, "Fee", SeedRows(assembly, app, "Fee")[0]));
        Assert.Contains(Validate(assembly, "Fee", New(assembly, "Fee", ("Amount", 922337203685477.5808m))),
            m => m.Contains("Amount must be"));
    }

    // --- S3: datetimeoffset ---

    [Fact]
    public void A_datetimeoffset_keeps_the_time_the_source_shows_and_says_its_offset_is_dropped()
    {
        Assert.Equal("0001-01-01T12:00:00.0000000",
            SqlValueText.Render(new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.FromHours(-12))));

        var value = SqlValueText.Render(new DateTimeOffset(2024, 3, 1, 23, 30, 0, TimeSpan.FromHours(-12)));
        var app = Generate(Database(add => add("Shift",
            [new("ShiftId", "int", false, IsPrimaryKey: true), new("StartsAt", "datetimeoffset", false)],
            [["1", value]])));

        var assembly = Compile(app);
        var row = SeedRows(assembly, app, "Shift")[0];

        // Whatever zone this machine is in: the seed names none, so nothing converts it.
        Assert.Equal(new DateTime(2024, 3, 1, 23, 30, 0), (DateTime)row.GetType().GetProperty("StartsAt")!.GetValue(row)!);
        Assert.Contains(app.Data.Notes, n => n.Contains("StartsAt") && n.Contains("offset is dropped"));
    }

    // --- S4: values the mapped type cannot hold ---

    [Fact]
    public void A_bigint_holding_values_beyond_int_keeps_every_value()
    {
        var app = Generate(Database(add => add("Counter",
            [
                new("CounterId", "int", false, IsPrimaryKey: true),
                new("Total", "bigint", false),
                new("Small", "bigint", false),
            ],
            [["1", "9223372036854775807", "5"], ["2", "-9223372036854775808", "6"]])));

        Assert.Equal("decimal", Property(app, "Counter", "Total").Type);
        Assert.Equal("int", Property(app, "Counter", "Small").Type);
        Assert.Empty(app.Unreadable);
        Assert.Contains(app.Data.Notes, n => n.Contains("Total") && n.Contains("every value kept"));

        var assembly = Compile(app);
        var rows = SeedRows(assembly, app, "Counter");
        Assert.Equal(9223372036854775807m, rows[0].GetType().GetProperty("Total")!.GetValue(rows[0]));
        Assert.All(rows, row => Assert.Empty(Validate(assembly, "Counter", row)));
        Assert.Contains(Validate(assembly, "Counter", New(assembly, "Counter", ("Total", 9223372036854775808m))),
            m => m.Contains("Total must be"));
    }

    [Fact]
    public void A_float_value_no_decimal_can_hold_is_left_empty_and_said_so_up_front()
    {
        var app = Generate(Database(add => add("Reading",
            [
                new("ReadingId", "int", false, IsPrimaryKey: true),
                new("Huge", "float", false, Precision: 53),
                new("Ratio", "real", false, Precision: 24),
            ],
            [["1", "1.79E+308", "0.5"], ["2", "2.5", "3.4E+38"]])));

        Assert.Contains(app.Data.Notes, n => n.Contains("Huge is float"));
        Assert.Contains(app.Data.Notes, n => n.Contains("Ratio is real"));

        // Left out, the row must not load a made-up 0 in its place: the value is missing.
        Assert.Equal("decimal?", Property(app, "Reading", "Huge").Type);
        Assert.Equal("decimal?", Property(app, "Reading", "Ratio").Type);

        var assembly = Compile(app);
        var first = SeedRows(assembly, app, "Reading")[0];
        Assert.Null(first.GetType().GetProperty("Huge")!.GetValue(first));
        Assert.Contains(app.Unreadable, n => n.Contains("1.79E+308"));
    }

    // --- S13: a SQL string is seeded exactly as stored ---

    [Fact]
    public void A_sql_string_is_seeded_exactly_except_a_fixed_width_pad()
    {
        var app = Generate(Database(add => add("Code",
            [
                new("CodeId", "int", false, IsPrimaryKey: true),
                new("Label", "varchar", false, 20),
                new("Short", "nchar", false, 3),
            ],
            [["1", "  lead", "NN "], ["2", "trail  ", "ABC"], ["3", "   ", "X  "]])));

        var rows = Seed(app, "Code");
        Assert.Equal(["  lead", "trail  ", "   "], rows.Select(r => r.GetProperty("Label").GetString()));
        Assert.Equal(["NN", "ABC", "X"], rows.Select(r => r.GetProperty("Short").GetString()));

        var assembly = Compile(app);
        Assert.All(SeedRows(assembly, app, "Code"), row => Assert.Empty(Validate(assembly, "Code", row)));
    }

    [Fact]
    public void A_file_string_is_still_trimmed()
    {
        var table = new TabularTable { Name = "Notes", SourceLabel = "notes.csv" };
        table.Columns.Add(new TabularColumn { Name = "Text" });
        table.Columns.Add(new TabularColumn { Name = "Other" });
        table.Rows.Add(["  padded  ", "x"]);

        var model = DataModelBuilder.Build([table]);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
        var json = SeedFileWriter.Files(classes, classNames, TabularImport.ToImportedData([table])).Single().Json;

        Assert.Equal("padded", JsonDocument.Parse(json).RootElement[0].GetProperty("Text").GetString());
    }

    // --- S2: a column the schema declares NULL may be empty ---

    [Fact]
    public void A_column_declared_null_is_nullable_even_when_no_row_read_is_null()
    {
        var app = Generate(Database(add =>
        {
            add("Ticket",
                [
                    new("TicketId", "int", false, IsPrimaryKey: true),
                    new("TicketNo", "int", true),
                    new("Small", "smalldatetime", true),
                    new("Num", "numeric", true, Precision: 18, Scale: 0),
                    new("Done", "bit", true),
                    new("Note", "nvarchar", true, 50),
                    new("Count", "int", false),
                ],
                [["1", "7001", "2024-01-02T00:00:00", "5", "true", "x", "3"]]);
        }));

        Assert.Equal("int?", Property(app, "Ticket", "TicketNo").Type);
        Assert.Equal("DateTime?", Property(app, "Ticket", "Small").Type);
        Assert.Equal("decimal?", Property(app, "Ticket", "Num").Type);
        Assert.Equal("bool?", Property(app, "Ticket", "Done").Type);
        Assert.Equal("int", Property(app, "Ticket", "Count").Type);
        Assert.DoesNotContain(Property(app, "Ticket", "Note").Rules, r => r.Name == "required");

        var assembly = Compile(app);
        Assert.Empty(Validate(assembly, "Ticket", New(assembly, "Ticket", ("Count", 1))));
    }

    // --- S11: two schemas, one table name ---

    [Fact]
    public void Tables_of_one_name_in_two_schemas_are_both_kept_and_said_so()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Note", 2, 1));
        source.TableList.Add(new SqlTableInfo("hr", "Note", 2, 1));
        source.TableList.Add(new SqlTableInfo("dbo", "Task", 3, 1));
        source.ColumnsByTable["dbo.Note"] = [new("NoteId", "int", false, IsPrimaryKey: true), new("Body", "nvarchar", false, 50)];
        source.ColumnsByTable["hr.Note"] = [new("NoteId", "int", false, IsPrimaryKey: true), new("Text", "nvarchar", false, 50)];
        source.ColumnsByTable["dbo.Task"] = [new("TaskId", "int", false, IsPrimaryKey: true), new("Title", "nvarchar", false, 50), new("HrNoteRef", "int", true)];
        source.RowsByTable["dbo.Note"] = [["10", "general"]];
        source.RowsByTable["hr.Note"] = [["20", "personnel"]];
        source.RowsByTable["dbo.Task"] = [["1", "Hire", "20"]];
        source.Keys.Add(new TabularForeignKey("HrNoteRef", "Note", "NoteId", "Task", "dbo", "hr"));

        var app = Generate(source);

        Assert.Equal(["Note", "HrNote", "Task"], app.Model.Entities.Select(e => e.Name));
        Assert.Equal("personnel", Seed(app, "HrNote")[0].GetProperty("Text").GetString());

        // Its key is still its own key, named for the table it was in the source.
        Assert.DoesNotContain(Class(app, "HrNote").Properties, p => p.Name == "NoteId");
        Assert.Equal("general", Seed(app, "Note")[0].GetProperty("Body").GetString());
        Assert.Contains(app.Data.Notes, n => n.Contains("hr.Note") && n.Contains("HrNote"));

        // The key follows the table it was declared against, under its new name.
        var task = app.Model.Entities.Single(e => e.Name == "Task");
        Assert.Equal("HrNote", Assert.Single(task.Relationships).Target);
        Compile(app);
    }

    [Fact]
    public void A_renamed_table_split_by_normalization_still_owns_its_key()
    {
        // A1: hr.Note keyed by NoteId, with a Tag1..Tag3 group the user splits off. Rewriting the
        // table lost its source name, so NoteId became a link to dbo.Note.
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Note", 2, 2));
        source.TableList.Add(new SqlTableInfo("hr", "Note", 5, 2));
        source.ColumnsByTable["dbo.Note"] = [new("NoteId", "int", false, IsPrimaryKey: true), new("Body", "nvarchar", false, 50)];
        source.ColumnsByTable["hr.Note"] =
        [
            new("NoteId", "int", false, IsPrimaryKey: true), new("Text", "nvarchar", false, 50),
            new("Tag1", "nvarchar", true, 20), new("Tag2", "nvarchar", true, 20), new("Tag3", "nvarchar", true, 20),
        ];
        source.RowsByTable["dbo.Note"] = [["1", "general"], ["2", "other"]];
        source.RowsByTable["hr.Note"] = [["2", "personnel", "a", "b", null], ["1", "payroll", "c", null, null]];

        var app = Generate(source, normalize: true);

        Assert.Contains(app.Model.Entities, e => e.Name == "HrNoteTag");
        var hrNote = app.Model.Entities.Single(e => e.Name == "HrNote");
        Assert.DoesNotContain(hrNote.Fields, f => f.Name == "NoteId");
        Assert.DoesNotContain(hrNote.Relationships, r => r.Target == "Note");
        Assert.DoesNotContain(Class(app, "HrNote").Properties, p => p.Name == "NoteId");
        Assert.Equal(["personnel", "payroll"], Seed(app, "HrNote").Select(r => r.GetProperty("Text").GetString()));
        Compile(app);
    }

    [Fact]
    public void A_table_whose_entity_name_is_taken_is_left_out_with_a_note()
    {
        var app = Generate(Database(add =>
        {
            add("Note", [new("NoteId", "int", false, IsPrimaryKey: true), new("Body", "nvarchar", false, 50)], [["1", "a"]]);
            add("Notes", [new("NotesId", "int", false, IsPrimaryKey: true), new("Text", "nvarchar", false, 50)], [["1", "b"]]);
        }));

        Assert.Equal(["Note"], app.Model.Entities.Select(e => e.Name));
        Assert.Contains(app.Data.Notes, n => n.Contains("dbo.Notes") && n.Contains("left out"));
    }

    // --- S9: DEFAULT values ---

    [Fact]
    public void A_literal_default_starts_every_new_record_and_a_null_stays_null()
    {
        var app = Generate(Database(add => add("Employee",
            [
                new("EmployeeId", "int", false, IsPrimaryKey: true),
                new("IsActive", "bit", false, Default: "((1))"),
                new("Status", "nvarchar", false, 20, Default: "(N'Active')"),
                new("Joined", "datetime", false, Default: "(getdate())"),
                new("Token", "uniqueidentifier", true, Default: "(newid())"),
                new("Rate", "decimal", true, Precision: 5, Scale: 2, Default: "((-2.5))"),
                new("Grade", "int", false, Default: "((3))"),
                new("Flag", "bit", true, Default: "((1))"),
                new("Odd", "int", false, Default: "(abs((-1)))"),
            ],
            [["1", "false", "Retired", "2020-01-01T00:00:00", null, null, "1", null, "4"]])));

        var assembly = Compile(app);
        var fresh = New(assembly, "Employee");
        object? Get(object item, string name) => item.GetType().GetProperty(name)!.GetValue(item);

        Assert.Equal(true, Get(fresh, "IsActive"));
        Assert.Equal("Active", Get(fresh, "Status"));
        Assert.InRange((DateTime)Get(fresh, "Joined")!, DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1));
        Assert.True(Guid.TryParse((string)Get(fresh, "Token")!, out _));
        Assert.Equal(-2.5m, Get(fresh, "Rate"));
        Assert.Equal(3, Get(fresh, "Grade"));
        Assert.Equal(0, Get(fresh, "Odd"));
        Assert.Contains(app.Data.Notes, n => n.Contains("Odd") && n.Contains("(abs((-1)))"));

        // A row the source stored NULL in is NULL, not the default a new record starts with.
        var row = SeedRows(assembly, app, "Employee")[0];
        Assert.Null(Get(row, "Flag"));
        Assert.Null(Get(row, "Rate"));
        Assert.Equal("", Get(row, "Token"));
        Assert.Equal(false, Get(row, "IsActive"));
        Assert.Equal("Retired", Get(row, "Status"));
    }

    // --- S8: CHECK constraints ---

    [Theory]
    [InlineData("([Age]>=(16) AND [Age]<=(99))", "Age", 16, 99)]
    [InlineData("([Grade]>=(1) AND [Grade]<=(10))", "Grade", 1, 10)]
    [InlineData("(Grade BETWEEN 1 AND 10)", "Grade", 1, 10)]
    [InlineData("([Qty]>(0))", "Qty", 0, null)]
    [InlineData("((0)<[Qty])", "Qty", 0, null)]
    [InlineData("([Qty] IS NULL OR [Qty]<(100))", "Qty", null, 100)]
    public void A_check_the_database_rewrote_is_read_as_bounds(string definition, string column, int? min, int? max)
    {
        var bounds = SqlCheck.Parse(definition)!;

        Assert.Equal((decimal?)min, bounds[column].Min);
        Assert.Equal((decimal?)max, bounds[column].Max);
    }

    [Theory]
    [InlineData("([Status]='Draft' OR [Status]='Retired' OR [Status]='Active')")]
    [InlineData("([Status] IN ('Active', 'Retired', 'Draft'))")]
    [InlineData("([Status]=N'Active' OR [Status]=N'Retired' OR [Status]=N'Draft' OR [Status] IS NULL)")]
    public void A_check_listing_values_is_read_as_the_list(string definition)
    {
        var bounds = SqlCheck.Parse(definition)!;

        Assert.Equal(["Active", "Draft", "Retired"], bounds["Status"].Allowed!.Order());
    }

    [Theory]
    [InlineData("(len([Code])=(3))")]
    [InlineData("([Ends]>[Starts])")]
    [InlineData("(NOT [Qty]=(0))")]
    [InlineData("([Qty]>(0) OR [Price]>(0))")]
    public void A_check_not_understood_is_not_read_at_all(string definition)
        => Assert.Null(SqlCheck.Parse(definition));

    [Fact]
    public void A_check_becomes_the_range_or_list_the_app_enforces()
    {
        var source = Database(add => add("Member",
            [
                new("MemberId", "int", false, IsPrimaryKey: true),
                new("Age", "tinyint", false),
                new("Grade", "smallint", false),
                new("Status", "nvarchar", false, 20),
                new("Qty", "int", false),
                new("Price", "decimal", false, Precision: 8, Scale: 2),
                new("Code", "nvarchar", false, 10),
            ],
            [["1", "30", "5", "Active", "2", "1.50", "ABC"]]));

        source.Constraints["Member"] =
        [
            new("CK_Age", SqlConstraintKind.Check, ["Age"], "([Age]>=(16) AND [Age]<=(99))"),
            new("CK_Grade", SqlConstraintKind.Check, ["Grade"], "([Grade]>=(1) AND [Grade]<=(10))"),
            new("CK_Status", SqlConstraintKind.Check, ["Status"], "([Status]='Draft' OR [Status]='Retired' OR [Status]='Active')"),
            new("CK_Qty", SqlConstraintKind.Check, ["Qty"], "([Qty]>(0))"),
            new("CK_Price", SqlConstraintKind.Check, ["Price"], "([Price]>(0))"),
            new("CK_Code", SqlConstraintKind.Check, ["Code"], "(len([Code])=(3))"),
        ];

        var app = Generate(source);
        var assembly = Compile(app);

        IReadOnlyList<string> Saving(params (string, object?)[] values)
            => Validate(assembly, "Member", New(assembly, "Member",
                [("Age", 30), ("Grade", 5), ("Status", "Active"), ("Qty", 2), ("Price", 1.50m), ("Code", "ABC"), .. values]));

        Assert.Empty(Saving());
        Assert.Contains(Saving(("Age", 15)), m => m.Contains("Age must be 16 or more"));
        Assert.Contains(Saving(("Age", 100)), m => m.Contains("Age must be 99 or less"));
        Assert.Contains(Saving(("Grade", 11)), m => m.Contains("Grade"));
        Assert.Contains(Saving(("Status", "Closed")), m => m.Contains("Status must be one of"));
        Assert.Contains(Saving(("Qty", 0)), m => m.Contains("Qty must be 1 or more"));
        Assert.Contains(Saving(("Price", 0m)), m => m.Contains("Price must be 0.01 or more"));
        Assert.Empty(Saving(("Price", 0.01m)));

        Assert.Contains(app.Data.Notes, n => n.Contains("CK_Code") && n.Contains("(len([Code])=(3))") && n.Contains("not carried"));
        Assert.All(SeedRows(assembly, app, "Member"), row => Assert.Empty(Validate(assembly, "Member", row)));
    }

    [Fact]
    public void A_check_range_on_an_int_foreign_key_is_not_enforced_on_the_renumbered_ids()
    {
        // A2: the CHECK bounds the source's key values (100..999); the app holds Ids 1 and 2.
        var source = Database(add =>
        {
            add("Department", [new("DepartmentId", "int", false, IsPrimaryKey: true), new("Title", "nvarchar", false, 20)],
                [["100", "Ops"], ["200", "Sales"]]);
            add("Employee",
                [new("EmployeeId", "int", false, IsPrimaryKey: true), new("Name", "nvarchar", false, 20), new("DepartmentId", "int", false)],
                [["1", "Ann", "200"], ["2", "Bob", "100"]]);
        });
        source.Keys.Add(new TabularForeignKey("DepartmentId", "Department", "DepartmentId", "Employee", "dbo", "dbo"));
        source.Constraints["Employee"] =
        [
            new("CK_Dept", SqlConstraintKind.Check, ["DepartmentId"], "([DepartmentID]>=(100) AND [DepartmentID]<=(999))"),
        ];

        var app = Generate(source);

        Assert.DoesNotContain(Property(app, "Employee", "DepartmentId").Rules, r => r.Name is "minvalue" or "maxvalue");

        var assembly = Compile(app);
        var rows = SeedRows(assembly, app, "Employee");
        Assert.Equal([2, 1], rows.Select(r => (int)r.GetType().GetProperty("DepartmentId")!.GetValue(r)!));
        Assert.All(rows, row => Assert.Empty(Validate(assembly, "Employee", row)));
    }

    [Fact]
    public void A_column_a_check_limits_to_a_list_is_not_proposed_unique()
    {
        // Three rows, three statuses: the data alone reads as "no two share Status", but the
        // CHECK allows only three values, so accepting that rule would refuse every new Product.
        var source = Database(add => add("Product",
            [
                new("ProductId", "int", false, IsPrimaryKey: true),
                new("Name", "nvarchar", false, 40),
                new("Status", "nvarchar", false, 20),
            ],
            [["1", "Pen", "Active"], ["2", "Ink", "Retired"], ["3", "Pad", "Draft"]]));

        source.Constraints["Product"] =
        [
            new("CK_Status", SqlConstraintKind.Check, ["Status"], "([Status]='Draft' OR [Status]='Retired' OR [Status]='Active')"),
        ];

        var product = Generate(source).Model.Entities.Single(e => e.Name == "Product");

        bool ProposedUnique(string field)
            => product.Fields.Single(f => f.Name == field).Proposals.Any(p => p.Kind == RuleProposalKind.Unique);

        Assert.False(ProposedUnique("Status"));
        Assert.True(ProposedUnique("Name"));
    }

    // --- S6, S7: unique constraints, unique indexes, composite keys ---

    /// <summary>A lookup over a fixed set of stored records, so a uniqueness check can collide.</summary>
    private static object Stored(Assembly assembly, params object[] items)
    {
        var lookup = assembly.GetType("Acme.Validation.ListLookup")!;
        return Activator.CreateInstance(lookup, [items])!;
    }

    [Fact]
    public void Declared_uniqueness_is_enforced_even_on_an_entity_with_links()
    {
        var source = Database(add =>
        {
            add("Dept", [new("DeptId", "int", false, IsPrimaryKey: true), new("Title", "nvarchar", false, 20)], [["1", "Ops"]]);
            add("Emp",
                [
                    new("EmpId", "int", false, IsPrimaryKey: true),
                    new("Email", "nvarchar", false, 50),
                    new("Badge", "int", true),
                    new("DeptId", "int", false),
                ],
                [["1", "a@x.com", "7", "1"], ["2", "b@x.com", "8", "1"]]);
        });
        source.Keys.Add(new TabularForeignKey("DeptId", "Dept", "DeptId", "Emp", "dbo", "dbo"));
        source.Constraints["Emp"] =
        [
            new("PK_Emp", SqlConstraintKind.PrimaryKey, ["EmpId"]),
            new("UQ_Emp_Email", SqlConstraintKind.Unique, ["Email"]),
            new("UX_Emp_Badge", SqlConstraintKind.Unique, ["Badge"]),
            new("UX_Emp_Mail", SqlConstraintKind.FilteredUnique, ["Email", "DeptId"], "([Badge] IS NOT NULL)"),
        ];

        var app = Generate(source);

        // Unique only where its filter holds: not enforced, and said so.
        Assert.Empty(Class(app, "Emp").UniqueTogether);
        Assert.Contains(app.Data.Notes, n => n.Contains("UX_Emp_Mail") && n.Contains("([Badge] IS NOT NULL)"));

        Assert.Contains(Property(app, "Emp", "Email").Rules, r => r.Name == "unique");
        Assert.Contains(Property(app, "Emp", "Badge").Rules, r => r.Name == "unique");
        Assert.DoesNotContain(Property(app, "Emp", "DeptId").Rules, r => r.Name == "unique");
        Compile(app);
    }

    [Fact]
    public void A_composite_key_refuses_a_second_row_with_the_same_pair()
    {
        var source = Database(add =>
        {
            add("Invoice", [new("InvoiceId", "int", false, IsPrimaryKey: true), new("Number", "nvarchar", false, 20)], [["1", "INV-1"]]);
            add("InvoiceLine",
                [new("InvoiceId", "int", false), new("LineNo", "int", false), new("Qty", "int", false)],
                [["1", "1", "5"], ["1", "2", "6"]]);
        });
        source.Keys.Add(new TabularForeignKey("InvoiceId", "Invoice", "InvoiceId", "InvoiceLine", "dbo", "dbo"));
        source.Constraints["InvoiceLine"] = [new("PK_InvoiceLine", SqlConstraintKind.PrimaryKey, ["InvoiceId", "LineNo"])];

        var app = Generate(source);

        // Neither column is unique on its own: only the pair is.
        Assert.DoesNotContain(Property(app, "InvoiceLine", "LineNo").Rules, r => r.Name == "unique");
        Assert.Equal(["InvoiceId", "LineNo"], Assert.Single(Class(app, "InvoiceLine").UniqueTogether));

        var assembly = Compile(app);
        var rows = SeedRows(assembly, app, "InvoiceLine");
        var lookup = Stored(assembly, [.. rows]);

        object Line(int id, int invoice, int line)
            => New(assembly, "InvoiceLine", ("Id", id), ("InvoiceId", invoice), ("LineNo", line), ("Qty", 1));

        Assert.Contains(ValidateWith(assembly, "InvoiceLine", Line(3, 1, 2), lookup), m => m.Contains("InvoiceId and LineNo"));
        Assert.Empty(ValidateWith(assembly, "InvoiceLine", Line(3, 1, 3), lookup));

        // A record saved again over itself is not its own duplicate.
        Assert.Empty(ValidateWith(assembly, "InvoiceLine", rows[1], lookup, "Update"));
    }

    // --- S10: a computed column ---

    [Fact]
    public void A_computed_column_is_left_out_and_its_expression_said()
    {
        var app = Generate(Database(add => add("Product",
            [
                new("ProductId", "int", false, IsPrimaryKey: true),
                new("PriceWithTax", "decimal", true, Precision: 19, Scale: 4, ComputedAs: "([UnitPrice]*(1.2))"),
                new("UnitPrice", "decimal", false, Precision: 18, Scale: 2),
                new("Title", "nvarchar", false, 20),
            ],
            [["1", "12.0000", "10.00", "Bolt"], ["2", "12.0000", "10.00", "Nut"]])));

        Assert.DoesNotContain(Class(app, "Product").Properties, p => p.Name == "PriceWithTax");
        Assert.Contains(app.Data.Notes, n => n.Contains("PriceWithTax") && n.Contains("([UnitPrice]*(1.2))"));

        // The columns after it still line up with their own values.
        Assert.Equal(["Bolt", "Nut"], Seed(app, "Product").Select(r => r.GetProperty("Title").GetString()));
        Assert.Equal(10.00m, Seed(app, "Product")[0].GetProperty("UnitPrice").GetDecimal());
        Compile(app);
    }

    // --- S15: a seeded date the app reads as not filled in ---

    [Fact]
    public void A_not_null_date_holding_year_one_is_said_to_fail_its_own_check()
    {
        var app = Generate(Database(add => add("Staff",
            [new("StaffId", "int", false, IsPrimaryKey: true), new("HiredOn", "date", false), new("Name", "nvarchar", false, 20)],
            [["1", "0001-01-01T00:00:00.0000000", "Ann"], ["2", "2020-05-01T00:00:00.0000000", "Bob"]])));

        Assert.Contains(app.Data.Notes, n => n.Contains("HiredOn") && n.Contains("0001-01-01") && n.Contains("1 row"));

        // The note is true: the seeded row is refused, the other is not.
        var assembly = Compile(app);
        var rows = SeedRows(assembly, app, "Staff");
        Assert.Contains(Validate(assembly, "Staff", rows[0]), m => m.Contains("HiredOn"));
        Assert.Empty(Validate(assembly, "Staff", rows[1]));
    }

    [Fact]
    public void A_file_date_filled_in_every_row_refuses_a_record_left_at_year_one()
    {
        // A8: a non-nullable DateTime with no check saved 0001-01-01 for a date never given.
        var table = DataIngestion.Schema.Readers.CsvReader.FromText("Chores", "chores.csv",
            "Title,Qty,DueOn\r\nWrite,1,2024-03-01\r\nTest,2,2024-04-15\r\n", ',');

        var app = Generate([table]);

        Assert.Equal("DateTime", Property(app, "Chore", "DueOn").Type);
        Assert.Contains(Property(app, "Chore", "DueOn").Rules, r => r.Name == "required");

        var assembly = Compile(app);
        Assert.All(SeedRows(assembly, app, "Chore"), row => Assert.Empty(Validate(assembly, "Chore", row)));
        Assert.Contains(Validate(assembly, "Chore", New(assembly, "Chore", ("Title", "x"), ("Qty", 1))),
            m => m.Contains("DueOn"));
    }

    // --- S12: a column whose name starts with a digit ---

    [Fact]
    public void A_column_named_with_a_leading_digit_is_kept_with_its_data()
    {
        var app = Generate(Database(add => add("Part",
            [new("PartId", "int", false, IsPrimaryKey: true), new("2ndRef", "nvarchar", true, 20), new("Title", "nvarchar", false, 20)],
            [["1", "B-7", "Bolt"]])));

        Assert.Equal("B-7", Seed(app, "Part")[0].GetProperty("Ref2nd").GetString());
        Assert.Contains(app.Model.Decisions, d => d.Name == "2ndRef" && d.Evidence!.Contains("became Ref2nd"));

        var assembly = Compile(app);
        Assert.Empty(Validate(assembly, "Part", SeedRows(assembly, app, "Part")[0]));
    }
}
