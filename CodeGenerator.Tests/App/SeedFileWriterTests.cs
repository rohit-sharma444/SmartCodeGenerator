using System.Text.Json;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedFileWriterTests
{
    private static ClassDefinition Order(params (string Name, string Type)[] properties)
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        foreach (var (name, type) in properties)
            definition.Properties.Add(new PropertyDefinition { Name = name, Type = type });

        return definition;
    }

    private static ImportedData Data(params Dictionary<string, string?>[] rows)
        => new(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["Order"] = rows.Cast<IReadOnlyDictionary<string, string?>>().ToList(),
            },
            []);

    private static Dictionary<string, string> Map()
        => new Dictionary<string, string> { ["Order"] = "Order" };

    private static JsonElement[] Rows(string json)
        => JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();

    [TestMethod]
    public void WritesOneFilePerEntityNamedAfterItsClass()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), Data(new Dictionary<string, string?> { ["Ref"] = "ORD-1" }));

        var file = files.Single();
        Assert.AreEqual("Order.json", file.FileName);
        Assert.HasCount(1, Rows(file.Json));
    }

    /// <summary>Ids are assigned 1..N in row order, which is what the foreign keys the readers and
    /// NormalizationPlan produced already point at.</summary>
    [TestMethod]
    public void IdsAreAssignedOneToNInRowOrder()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(),
            Data(
                new Dictionary<string, string?> { ["Ref"] = "a" },
                new Dictionary<string, string?> { ["Ref"] = "b" },
                new Dictionary<string, string?> { ["Ref"] = "c" }));

        Assert.AreEqual(
            "1,2,3",
            string.Join(",", Rows(files.Single().Json).Select(r => r.GetProperty("Id").GetInt32())));
    }

    [TestMethod]
    public void ValuesAreWrittenAsTheirDeclaredJsonTypes()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Qty", "int"), ("Total", "decimal"),
                   ("Paid", "bool"), ("Placed", "DateTime"))],
            Map(),
            Data(new Dictionary<string, string?>
            {
                ["Ref"] = "ORD-1",
                ["Qty"] = "2",
                ["Total"] = "9.99",
                ["Paid"] = "yes",
                ["Placed"] = "2026-01-15",
            }));

        var row = Rows(files.Single().Json)[0];

        Assert.AreEqual(JsonValueKind.String, row.GetProperty("Ref").ValueKind);
        Assert.AreEqual(2, row.GetProperty("Qty").GetInt32());
        Assert.AreEqual(9.99m, row.GetProperty("Total").GetDecimal());
        Assert.IsTrue(row.GetProperty("Paid").GetBoolean());
        Assert.AreEqual(new DateTime(2026, 1, 15), row.GetProperty("Placed").GetDateTime());
    }

    /// <summary>
    /// THE review-loop interaction. The writer walks the CLASS's properties, so a field the user
    /// removed simply stops being written -- the seed file can never name a property the generated
    /// class does not declare, which would not compile in the generated tests and would throw at
    /// load.
    /// </summary>
    [TestMethod]
    public void AFieldTheUserRemovedIsNotWritten()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))],   // Qty was removed in the review loop
            Map(),
            Data(new Dictionary<string, string?> { ["Ref"] = "ORD-1", ["Qty"] = "2" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Qty", out _));
    }

    /// <summary>A field the user ADDED has no imported value, so it is omitted and the class's
    /// default stands.</summary>
    [TestMethod]
    public void AFieldWithNoImportedValueIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Courier", "string"))],
            Map(),
            Data(new Dictionary<string, string?> { ["Ref"] = "ORD-1" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Courier", out _));
    }

    /// <summary>A value that does not parse as its declared type is omitted, not emitted as a
    /// broken literal. The user changed the type in the review loop and the data disagrees; the
    /// row still loads.</summary>
    [TestMethod]
    public void AValueThatDoesNotParseAsItsTypeIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Qty", "int"))], Map(), Data(new Dictionary<string, string?> { ["Qty"] = "several" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Qty", out _));
    }

    /// <summary>Omitted, but never silently: the user's own value is not in the app, and the only
    /// way they learn that is by being told which field lost what.</summary>
    [TestMethod]
    public void AnOmittedValueIsReportedWithItsFieldAndType()
    {
        SeedFileWriter.Files(
            [Order(("Qty", "int"))], Map(),
            Data(
                new Dictionary<string, string?> { ["Qty"] = "several" },
                new Dictionary<string, string?> { ["Qty"] = "2" },
                new Dictionary<string, string?> { ["Qty"] = "lots" }),
            out var unreadable);

        var note = unreadable.Single();
        StringAssert.Contains(note, "Order.Qty");
        StringAssert.Contains(note, "2 value(s)");
        StringAssert.Contains(note, "int");
        StringAssert.Contains(note, "\"several\"");
    }

    [TestMethod]
    public void ValuesThatAllParseReportNothing()
    {
        SeedFileWriter.Files(
            [Order(("Qty", "int"), ("Note", "string"))], Map(),
            Data(new Dictionary<string, string?> { ["Qty"] = "2", ["Note"] = "several" }),
            out var unreadable);

        Assert.IsEmpty(unreadable);
    }

    /// <summary>
    /// The seed parser used to be LOOSER than inference: it allowed thousands separators, so a
    /// column the user retyped to decimal turned "1,2" into twelve. It now reads exactly what
    /// inference would, and anything else is reported rather than reinterpreted.
    /// </summary>
    [TestMethod]
    public void AThousandsSeparatedValueIsNotReadAsANumber()
    {
        var files = SeedFileWriter.Files(
            [Order(("Total", "decimal"))], Map(),
            Data(new Dictionary<string, string?> { ["Total"] = "1,2" }),
            out var unreadable);

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Total", out _));
        Assert.HasCount(1, unreadable);
    }

    /// <summary>DateTime.TryParse read a slash date month-first, so a UK 01/02/2026 retyped to
    /// DateTime became 2 January. Only the unambiguous formats inference accepts are read.</summary>
    [TestMethod]
    public void ASlashDateIsNotReadMonthFirst()
    {
        var files = SeedFileWriter.Files(
            [Order(("Placed", "DateTime"))], Map(),
            Data(new Dictionary<string, string?> { ["Placed"] = "01/02/2026" }),
            out var unreadable);

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Placed", out _));
        Assert.HasCount(1, unreadable);
    }

    /// <summary>A SQL float renders with an exponent ("1E-05"), and the decimal parser refused
    /// it, so every small or large float in a float column was dropped.</summary>
    [TestMethod]
    public void AnExponentFloatIsReadAsItsValue()
    {
        var row = Rows(SeedFileWriter.Files(
            [Order(("Rate", "decimal"))], Map(),
            Data(new Dictionary<string, string?> { ["Rate"] = "1E-05" })).Single().Json)[0];

        Assert.AreEqual(0.00001m, row.GetProperty("Rate").GetDecimal());
    }

    /// <summary>A SQL bigint is typed int, and a value past int.MaxValue cannot be held by one.
    /// That used to be one warning about the column and then silence about every value lost.</summary>
    [TestMethod]
    public void AnIntegerTooLargeForIntIsReported()
    {
        SeedFileWriter.Files(
            [Order(("Reference", "int"))], Map(),
            Data(new Dictionary<string, string?> { ["Reference"] = "3000000000" }),
            out var unreadable);

        StringAssert.Contains(unreadable.Single(), "3000000000");
    }

    /// <summary>The round-trip form SQL Server's rows are rendered in, with a fraction and an
    /// offset, is a date and must be read as one.</summary>
    [TestMethod]
    public void ASqlRoundTripDateWithAnOffsetIsRead()
    {
        SeedFileWriter.Files(
            [Order(("Placed", "DateTime"))], Map(),
            Data(new Dictionary<string, string?> { ["Placed"] = "2026-01-02T10:00:00.1234567+05:00" }),
            out var unreadable);

        Assert.IsEmpty(unreadable);
    }

    /// <summary>
    /// Inference and the seed writer share one parse definition, so whatever type inference picks
    /// for a column, every value in it is written. Before, inference accepted forms the writer did
    /// not ("1E5"), and the writer accepted forms inference did not ("1,2").
    /// </summary>
    [TestMethod]
    [DataRow("1", "-2", "+3")]
    [DataRow("1.5", "2", "-0.25", "1E5", "2.5e-3")]
    [DataRow("true", "no", "Y")]
    [DataRow("2026-01-15", "2026/02/01", "2026-01-15 09:30", "15 Jan 2026", "January 5, 2026",
        "2026-01-15T09:30:00Z", "2026-01-15T09:30:00.123+05:00")]
    public void EveryValueOfAnInferredColumnIsWritten(params string[] values)
    {
        var type = DataIngestion.Schema.ColumnTypeInference.Infer(values, "Whatever");
        Assert.AreNotEqual("string", type);

        var rows = Rows(SeedFileWriter.Files(
            [Order(("Value", type))], Map(),
            Data(values.Select(v => new Dictionary<string, string?> { ["Value"] = v }).ToArray()),
            out var unreadable).Single().Json);

        Assert.IsEmpty(unreadable);
        Assert.IsTrue(rows.All(r => r.TryGetProperty("Value", out _)));
    }

    [TestMethod]
    public void ANullValueIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Note", "string"))],
            Map(),
            Data(new Dictionary<string, string?> { ["Ref"] = "ORD-1", ["Note"] = null }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Note", out _));
    }

    /// <summary>Navigation properties are resolved from the foreign key at load time. Writing them
    /// would make Order -> Customer -> Orders a reference cycle, which is the reason
    /// DomainModelMapper marks them [JsonIgnore] in the first place.</summary>
    [TestMethod]
    public void NavigationPropertiesAreNeverWritten()
    {
        var order = Order(("Ref", "string"), ("CustomerId", "int"));
        order.Properties.Add(new PropertyDefinition { Name = "Customer", Type = "Customer" });
        order.Properties.Add(new PropertyDefinition { Name = "Lines", Type = "List<OrderLine>" });

        var row = Rows(SeedFileWriter.Files(
            [order],
            Map(),
            Data(new Dictionary<string, string?>
            {
                ["Ref"] = "a",
                ["CustomerId"] = "1",
            })).Single().Json)[0];

        Assert.IsFalse(row.TryGetProperty("Customer", out _));
        Assert.IsFalse(row.TryGetProperty("Lines", out _));
        Assert.AreEqual(1, row.GetProperty("CustomerId").GetInt32());
    }

    /// <summary>Rows are keyed by ENTITY name and files by CLASS name, and ToUniqueClassName can
    /// rename one. Without the map the rows would go to a file no class ever reads.</summary>
    [TestMethod]
    public void ARenamedClassGetsItsEntitysRows()
    {
        var definition = new ClassDefinition { Name = "TypeItem" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Name", Type = "string" });

        var data = new ImportedData(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["Type"] = new List<IReadOnlyDictionary<string, string?>>
                {
                    new Dictionary<string, string?> { ["Name"] = "Hardback" },
                },
            },
            []);

        var file = SeedFileWriter.Files(
            [definition], new Dictionary<string, string> { ["Type"] = "TypeItem" }, data).Single();

        Assert.AreEqual("TypeItem.json", file.FileName);
        Assert.AreEqual("Hardback", Rows(file.Json)[0].GetProperty("Name").GetString());
    }

    [TestMethod]
    public void AClassWithNoImportedRowsGetsNoFile()
        => Assert.IsEmpty(SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), ImportedData.None));

    /// <summary>A value that looks like JSON or C# is data, not syntax. It goes through the JSON
    /// writer, so it can never break out of its string.</summary>
    [TestMethod]
    public void AValueContainingQuotesAndBracesStaysOneString()
    {
        var hostile = "\"},{\"Id\":99,\"Ref\":\"injected";

        var rows = Rows(SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), Data(new Dictionary<string, string?> { ["Ref"] = hostile })).Single().Json);

        Assert.HasCount(1, rows);
        Assert.AreEqual(hostile, rows[0].GetProperty("Ref").GetString());
    }
}
