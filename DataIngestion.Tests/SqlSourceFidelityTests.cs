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

    private static Generated Generate(FakeSqlSchemaSource source, Action<CandidateDomainModel>? review = null)
    {
        var tables = Read(source);
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
    {
        var type = assembly.GetType("Acme.Models." + className)!;
        var validatorType = assembly.GetType($"Acme.Validation.{className}Validator")!;
        var context = assembly.GetType("Acme.Validation.ValidationContext`1")!.MakeGenericType(type)
            .GetMethod("For")!
            .Invoke(null, [item, Enum.Parse(assembly.GetType("Acme.Validation.ValidationOperation")!, operation),
                Activator.CreateInstance(assembly.GetType("Acme.Validation.StoreEntityLookup")!)]);
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
