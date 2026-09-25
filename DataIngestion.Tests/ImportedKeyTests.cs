using System.Text.Json;
using AppScaffolding;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// A source's own keys are dropped and every seeded row is numbered 1..N instead, so a foreign
/// key read from the same source has to be rewritten to the number its parent row was given.
/// Left as it was read, it points at whichever row happens to hold that position -- a different
/// parent, or none at all.
/// </summary>
/// <remarks>
/// Every test here runs the whole import path -- DataModelBuilder, DomainModelMapper,
/// TabularImport and SeedFileWriter -- and reads the seed files it writes, because the defect is
/// in how those four agree, not in any one of them.
/// </remarks>
public class ImportedKeyTests
{
    private static TabularTable Table(
        string name, (string Name, string Type)[] columns, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var (column, type) in columns)
            table.Columns.Add(new TabularColumn { Name = column, Type = type });

        foreach (var row in rows)
            table.Rows.Add(row);

        return table;
    }

    /// <summary>Class name -> the rows its seed file holds, as written.</summary>
    private static Dictionary<string, JsonElement[]> Seeds(IReadOnlyList<TabularTable> tables)
    {
        var model = DataModelBuilder.Build(tables);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);

        return SeedFileWriter.Files(classes, classNames, TabularImport.ToImportedData(tables))
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f.FileName),
                f => JsonDocument.Parse(f.Json).RootElement.EnumerateArray()
                    .Select(e => e.Clone()).ToArray());
    }

    /// <summary>The value of <paramref name="field"/> on each <paramref name="parent"/> row an
    /// order points at, keyed by the order's Ref; null for an order that points at nothing.</summary>
    private static Dictionary<string, string?> ParentOf(
        Dictionary<string, JsonElement[]> seeds, string child, string key, string parent, string field)
    {
        var byId = seeds[parent].ToDictionary(
            r => r.GetProperty("Id").GetInt32(), r => r.GetProperty(field).GetString());

        return seeds[child].ToDictionary(
            r => r.GetProperty("Ref").GetString()!,
            r => r.TryGetProperty(key, out var id) && byId.TryGetValue(id.GetInt32(), out var name)
                ? name
                : null);
    }

    private static readonly (string, string)[] CustomerColumns =
        [("CustomerId", "int"), ("Name", "string")];

    private static readonly (string, string)[] OrderColumns =
        [("OrderId", "int"), ("CustomerId", "int"), ("Ref", "string")];

    /// <summary>
    /// The worst case: keys that happen to fall inside 1..N. Every order still has a customer,
    /// just the wrong one, and nothing anywhere says so.
    /// </summary>
    [Fact]
    public void A_foreign_key_points_at_the_parent_the_source_named_not_the_one_at_that_position()
    {
        var customers = Table("Customers", CustomerColumns, ["3", "Ada"], ["1", "Bob"], ["2", "Cy"]);
        var orders = Table("Orders", OrderColumns,
            ["1", "1", "ORD-BOB"], ["2", "2", "ORD-CY"], ["3", "3", "ORD-ADA"]);

        var owners = ParentOf(Seeds([customers, orders]), "Order", "CustomerId", "Customer", "Name");

        Assert.Equal("Bob", owners["ORD-BOB"]);
        Assert.Equal("Cy", owners["ORD-CY"]);
        Assert.Equal("Ada", owners["ORD-ADA"]);
    }

    /// <summary>Keys past N: without the rewrite every order loses its customer.</summary>
    [Fact]
    public void A_foreign_key_to_a_key_past_the_row_count_still_finds_its_parent()
    {
        var customers = Table("Customers", CustomerColumns, ["300", "Ada"], ["100", "Bob"]);
        var orders = Table("Orders", OrderColumns, ["1", "100", "ORD-BOB"], ["2", "300", "ORD-ADA"]);

        var owners = ParentOf(Seeds([customers, orders]), "Order", "CustomerId", "Customer", "Name");

        Assert.Equal("Bob", owners["ORD-BOB"]);
        Assert.Equal("Ada", owners["ORD-ADA"]);
    }

    /// <summary>A key no parent carries is left unset and said, and the note does not blame a
    /// row cap that never applied.</summary>
    [Fact]
    public void A_foreign_key_naming_no_parent_is_cleared_and_said_without_blaming_the_cap()
    {
        var customers = Table("Customers", CustomerColumns, ["100", "Bob"]);
        var orders = Table("Orders", OrderColumns, ["1", "100", "ORD-BOB"], ["2", "999", "ORD-NOBODY"]);

        var tables = new[] { customers, orders };
        var owners = ParentOf(Seeds(tables), "Order", "CustomerId", "Customer", "Name");

        Assert.Equal("Bob", owners["ORD-BOB"]);
        Assert.Null(owners["ORD-NOBODY"]);

        var note = Assert.Single(
            TabularImport.ToImportedData(tables).Notes, n => n.Contains("could not be linked"));
        Assert.DoesNotContain($"first {ImportedData.MaxSeedRows}", note);
    }

    /// <summary>Two parents with the same key: which one was meant is unknown, so the reference
    /// is cleared rather than guessed.</summary>
    [Fact]
    public void A_foreign_key_to_a_key_two_parents_share_is_cleared_not_guessed()
    {
        var customers = Table("Customers", CustomerColumns, ["7", "Ada"], ["7", "Bob"], ["8", "Cy"]);
        var orders = Table("Orders", OrderColumns, ["1", "7", "ORD-AMBIGUOUS"], ["2", "8", "ORD-CY"]);

        var owners = ParentOf(Seeds([customers, orders]), "Order", "CustomerId", "Customer", "Name");

        Assert.Null(owners["ORD-AMBIGUOUS"]);
        Assert.Equal("Cy", owners["ORD-CY"]);
    }

    /// <summary>
    /// A JSON child's key is the parent's ROW NUMBER, not a value read from the source -- so it
    /// must be left alone even when the parent carries an id of its own. Rewriting it through
    /// that id would clear every one of them.
    /// </summary>
    [Fact]
    public void A_json_child_key_is_a_row_number_and_is_not_rewritten_through_the_parents_id()
    {
        var tables = JsonTableReader.ReadJson(
            """
            [
              { "id": 10, "ref": "ORD-A", "lines": [ { "sku": "NUT" } ] },
              { "id": 20, "ref": "ORD-B", "lines": [ { "sku": "BOLT" } ] }
            ]
            """,
            "Orders", "orders.json");

        var seeds = Seeds(tables);

        var refById = seeds["Order"].ToDictionary(
            r => r.GetProperty("Id").GetInt32(), r => r.GetProperty("Ref").GetString());

        var orderOf = seeds["Line"].ToDictionary(
            r => r.GetProperty("Sku").GetString()!,
            r => refById[r.GetProperty("OrderId").GetInt32()]);

        Assert.Equal("ORD-A", orderOf["NUT"]);
        Assert.Equal("ORD-B", orderOf["BOLT"]);
    }

    /// <summary>A key held as text is the user's data, not a row number, and is written exactly as
    /// it was read.</summary>
    [Fact]
    public void A_text_foreign_key_is_left_as_it_was_read()
    {
        var customers = Table("Customers", [("CustomerId", "string"), ("Name", "string")],
            ["ALFKI", "Ada"]);
        var orders = Table("Orders", [("CustomerId", "string"), ("Ref", "string")],
            ["ALFKI", "ORD-1"]);

        var row = TabularImport.ToImportedData([customers, orders]).For("Order")[0];

        Assert.Equal("ALFKI", row["CustomerId"]);
    }
}
