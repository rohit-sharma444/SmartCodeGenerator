using DataIngestion.Normalization;
using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class NormalizationPlanTests
{
    private static TabularTable Orders(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    private static IReadOnlyList<TabularTable> Apply(TabularTable table, NormalizationFindingKind kind)
        => NormalizationPlan.Apply(
            [table],
            NormalizationAnalyzer.Analyze(table).Findings.Where(f => f.Kind == kind).ToList());

    /// <summary>
    /// A rewritten table is still the table the source described. Rebuilt with only its name and
    /// columns, a partly-read SQL table stopped saying so (its rules then read as holding in every
    /// row), its declared foreign keys vanished, and every column lost what the database declared
    /// about it -- the lengths and nullability the generated validators are built from.
    /// </summary>
    [Fact]
    public void ARewrittenTableKeepsWhatTheSourceDeclaredAboutIt()
    {
        var facts = new CodeGenerationEngine.Models.TypeFacts { MaxLength = 20, IsNullable = false };

        var table = new TabularTable
        {
            Name = "Orders", SourceLabel = "dbo.Orders", IsPartial = true, SourceRowCount = 47_000_000
        };
        table.Columns.Add(new TabularColumn { Name = "Ref", Facts = facts });
        table.Columns.Add(new TabularColumn { Name = "CustomerId", Type = "int" });
        table.Columns.Add(new TabularColumn { Name = "Total", Type = "decimal" });
        table.Columns.Add(new TabularColumn { Name = "WarehouseId", Type = "int" });
        table.Rows.Add(["ORD-1", "1", "9.99", "4"]);
        table.ForeignKeys.Add(new TabularForeignKey("CustomerId", "Customers", "CustomerId"));
        table.ForeignKeys.Add(new TabularForeignKey("WarehouseId", "Warehouses", "WarehouseId"));

        var dropWarehouse = new NormalizationFinding
        {
            Kind = NormalizationFindingKind.DerivedColumn,
            TableName = "Orders",
            SourceLabel = "dbo.Orders",
            Columns = ["WarehouseId"],
            Headline = "Derived column: WarehouseId",
            Proposal = "drop it",
            Evidence = [],
        };

        var rewritten = NormalizationPlan.Apply([table], [dropWarehouse]).Single();

        Assert.True(rewritten.IsPartial);
        Assert.Equal(47_000_000, rewritten.SourceRowCount);
        Assert.Same(facts, rewritten.Columns.Single(c => c.Name == "Ref").Facts);

        // A declared key on a column that is gone goes with it; the rest stay.
        Assert.Equal(
            new[] { "CustomerId" }, rewritten.ForeignKeys.Select(k => k.Column));
    }

    [Fact]
    public void ARepeatingGroupBecomesAChildTableAndLeavesTheParent()
    {
        var tables = Apply(
            Orders(["Ref", "Item1", "Item2"],
                ["ORD-1", "Nut", "Bolt"],
                ["ORD-2", "Washer", null]),
            NormalizationFindingKind.RepeatingGroup);

        Assert.Equal(2, tables.Count);

        var parent = tables[0];
        Assert.Equal(new[] { "Ref" }, parent.Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("OrderItem", child.Name);
        Assert.Equal(new[] { "OrderId", "Item" }, child.Columns.Select(c => c.Name));

        // One row per non-empty cell; the empty Item2 of row 2 contributes nothing.
        Assert.Equal(3, child.Rows.Count);
        Assert.Equal(new[] { "1", "1", "2" },
            child.Rows.Select(r => r[child.IndexOf("OrderId")]));
        Assert.Equal(new[] { "Nut", "Bolt", "Washer" },
            child.Rows.Select(r => r[child.IndexOf("Item")]));
        Assert.Equal("int", child.Columns.Single(c => c.Name == "OrderId").Type);
    }

    [Fact]
    public void ANonAtomicColumnBecomesAChildTableOfItsParts()
    {
        var tables = Apply(
            Orders(["Ref", "Tags"],
                ["ORD-1", "rush,gift"],
                ["ORD-2", "rush,fragile"],
                ["ORD-3", "gift,fragile"]),
            NormalizationFindingKind.NonAtomicValues);

        Assert.Equal(new[] { "Ref" }, tables[0].Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("Tag", child.Name);
        Assert.Equal(new[] { "OrderId", "Value" }, child.Columns.Select(c => c.Name));
        Assert.Equal(6, child.Rows.Count);
        Assert.Equal("rush", child.Value(0, "Value"));
        Assert.Equal("3", child.Value(5, "OrderId"));
    }

    [Fact]
    public void APartialDependencyBecomesAParentTableTheOriginalReferences()
    {
        var tables = NormalizationPlan.Apply(
            [Orders(["Ref", "CustomerName", "CustomerCity"],
                ["ORD-1", "Ada", "London"],
                ["ORD-2", "Grace", "York"],
                ["ORD-3", "Ada", "London"],
                ["ORD-4", "Grace", "York"],
                ["ORD-5", "Ada", "London"])],
            NormalizationAnalyzer.Analyze(
                Orders(["Ref", "CustomerName", "CustomerCity"],
                    ["ORD-1", "Ada", "London"],
                    ["ORD-2", "Grace", "York"],
                    ["ORD-3", "Ada", "London"],
                    ["ORD-4", "Grace", "York"],
                    ["ORD-5", "Ada", "London"])).Findings);

        var order = tables.Single(t => t.Name == "Orders");
        var customer = tables.Single(t => t.Name == "Customer");

        // The extracted columns leave the parent, replaced by a foreign key.
        Assert.Equal(new[] { "Ref", "CustomerId" }, order.Columns.Select(c => c.Name));
        Assert.Equal("int", order.Columns.Single(c => c.Name == "CustomerId").Type);

        // One row per DISTINCT determinant value, in first-appearance order.
        Assert.Equal(new[] { "CustomerName", "CustomerCity" }, customer.Columns.Select(c => c.Name));
        Assert.Equal(2, customer.Rows.Count);
        Assert.Equal("Ada", customer.Value(0, "CustomerName"));
        Assert.Equal("Grace", customer.Value(1, "CustomerName"));

        // ...and the parent's FK points at the right one.
        Assert.Equal(new[] { "1", "2", "1", "2", "1" },
            order.Rows.Select(r => r[order.IndexOf("CustomerId")]));
    }

    [Fact]
    public void ADerivedColumnIsDroppedWithNoNewTable()
    {
        var tables = Apply(
            Orders(["Qty", "UnitPrice", "Total"],
                ["2", "5.00", "10.00"],
                ["3", "2.50", "7.50"],
                ["4", "1.25", "5.00"],
                ["5", "2.00", "10.00"],
                ["6", "0.50", "3.00"]),
            NormalizationFindingKind.DerivedColumn);

        var parent = Assert.Single(tables);
        Assert.Equal(new[] { "Qty", "UnitPrice" }, parent.Columns.Select(c => c.Name));
    }

    /// <summary>Applying nothing changes nothing -- this is the `k` (keep as-is) path.</summary>
    [Fact]
    public void ApplyingNoFindingsReturnsTheTablesUnchanged()
    {
        var table = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);

        var result = Assert.Single(NormalizationPlan.Apply([table], []));
        Assert.Equal(new[] { "Ref", "Item1", "Item2" }, result.Columns.Select(c => c.Name));
    }

    /// <summary>A findings list is a subset the user chose, so applying one of two must not
    /// silently apply the other.</summary>
    [Fact]
    public void OnlyTheChosenFindingsAreApplied()
    {
        var table = Orders(["Ref", "Item1", "Item2", "Tags"],
            ["ORD-1", "Nut", "Bolt", "rush,gift"],
            ["ORD-2", "Washer", null, "rush,fragile"],
            ["ORD-3", "Nut", null, "gift,fragile"]);

        var repeating = NormalizationAnalyzer.Analyze(table).Findings
            .Single(f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        var tables = NormalizationPlan.Apply([table], [repeating]);

        Assert.Equal(2, tables.Count);
        Assert.Contains("Tags", tables[0].Columns.Select(c => c.Name));
    }

    /// <summary>A table not named by any finding passes straight through -- JSON yields several
    /// tables and only some of them will have findings.</summary>
    [Fact]
    public void TablesWithNoFindingsPassThroughUntouched()
    {
        var withFinding = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);
        var other = RepeatingGroupTests.Table("Members", ["Name"], ["Ada"]);

        var tables = NormalizationPlan.Apply(
            [withFinding, other],
            NormalizationAnalyzer.Analyze(withFinding).Findings
                .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList());

        Assert.Contains(tables, t => t.Name == "Members" && t.Columns.Count == 1);
    }

    /// <summary>A child's name must not collide with a table already in the run, or the model
    /// builder would drop the second as a duplicate and the split would silently vanish.</summary>
    [Fact]
    public void AChildNameAlreadyTakenIsSuffixedRatherThanColliding()
    {
        var orders = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);
        var existing = RepeatingGroupTests.Table("OrderItem", ["Sku"], ["A"]);

        var tables = NormalizationPlan.Apply(
            [orders, existing],
            NormalizationAnalyzer.Analyze(orders).Findings
                .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList());

        Assert.Contains(tables, t => t.Name == "OrderItem2");
        Assert.Contains(tables, t => t.Name == "OrderItem");
    }
}
