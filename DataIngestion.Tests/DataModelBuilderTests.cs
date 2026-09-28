using AppScaffolding;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests;

public class DataModelBuilderTests
{
    private static TabularTable Table(string name, params (string Column, string Type)[] columns)
    {
        var table = new TabularTable { Name = name, SourceLabel = name.ToLowerInvariant() + ".csv" };
        foreach (var (column, type) in columns)
            table.Columns.Add(new TabularColumn { Name = column, Type = type });

        // One row, so the table is never empty; values are irrelevant to model building.
        table.Rows.Add(columns.Select(_ => (string?)"x").ToArray());
        return table;
    }

    [Fact]
    public void EachTableBecomesAnEntityCarryingItsColumnsAsTypedFields()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("Ref", "string"), ("Qty", "int"), ("Total", "decimal"))]);

        var order = Assert.Single(model.Entities);
        Assert.Equal("Order", order.Name);
        Assert.Equal(Provenance.Data, order.Provenance);
        Assert.Equal(new[] { "Ref", "Qty", "Total" }, order.Fields.Select(f => f.Name));
        Assert.Equal(new[] { "string", "int", "decimal" }, order.Fields.Select(f => f.Type));
        Assert.All(order.Fields, f => Assert.Equal(Provenance.Data, f.Provenance));
    }

    /// <summary>
    /// DomainModelMapper prepends its own `int Id`. Keeping a read Id would leave a string key
    /// that compiles and then makes JsonStore -- which assigns Id by reflection -- throw on the
    /// first Add. ModelEditor.AddField refuses an Id field for exactly this reason.
    /// </summary>
    [Theory]
    [InlineData("Id")]
    [InlineData("id")]
    [InlineData("OrderId")]
    public void AKeyColumnIsDropped_BecauseIdIsGenerated(string column)
    {
        var model = DataModelBuilder.Build([Table("Orders", (column, "int"), ("Ref", "string"))]);

        var order = Assert.Single(model.Entities);
        Assert.Equal(new[] { "Ref" }, order.Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Name == column && d.Reason == DecisionReasons.PrimaryKeyIsGenerated);
    }

    /// <summary>
    /// A `&lt;Other&gt;Id` column is a foreign key, not this entity's own key, so it is kept AND
    /// paired with a relationship -- which is what makes DomainModelMapper emit the nav property.
    /// </summary>
    [Fact]
    public void AForeignKeyColumnIsKeptAndBecomesARelationship()
    {
        var model = DataModelBuilder.Build(
        [
            Table("Customers", ("Name", "string")),
            Table("Orders", ("CustomerId", "int"), ("Ref", "string")),
        ]);

        var order = model.Entities.Single(e => e.Name == "Order");

        Assert.Contains("CustomerId", order.Fields.Select(f => f.Name));
        var relationship = Assert.Single(order.Relationships);
        Assert.Equal("Customer", relationship.Target);
        Assert.Equal("references", relationship.Role);
    }

    /// <summary>A `&lt;Other&gt;Id` naming no table in this run is an incidental int, not a
    /// foreign key, and must not acquire a relationship to a class that will never exist.</summary>
    [Fact]
    public void AnIdColumnNamingNoTable_GetsNoRelationship()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("LegacyId", "int"), ("Ref", "string"))]);

        var order = Assert.Single(model.Entities);
        Assert.Contains("LegacyId", order.Fields.Select(f => f.Name));
        Assert.Empty(order.Relationships);
    }

    /// <summary>`class Product { public string Product { get; set; } }` is CS0542.</summary>
    [Fact]
    public void AColumnNamedAfterItsOwnClassIsRenamed()
    {
        var model = DataModelBuilder.Build([Table("Products", ("Product", "string"), ("Sku", "string"))]);

        var product = Assert.Single(model.Entities);
        Assert.Equal("Product", product.Name);
        Assert.Contains("ProductName", product.Fields.Select(f => f.Name));
        Assert.DoesNotContain("Product", product.Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Reason == DecisionReasons.ColumnWouldCollideWithItsClassName);
    }

    /// <summary>...and if the renamed form is taken too, fall further back rather than
    /// producing two fields with one name.</summary>
    [Fact]
    public void TheRenameFallsBackAgainWhenItsFirstChoiceIsTaken()
    {
        var model = DataModelBuilder.Build(
            [Table("Products", ("Product", "string"), ("ProductName", "string"))]);

        var product = Assert.Single(model.Entities);
        Assert.Equal(new[] { "ProductValue", "ProductName" }.OrderBy(n => n),
            product.Fields.Select(f => f.Name).OrderBy(n => n));
    }

    /// <summary>`class Order { public string Equals { get; set; } }` hides
    /// <c>object.Equals</c> and emits CS0108.</summary>
    [Fact]
    public void AColumnNamedAfterAnInheritedObjectMemberIsRenamed()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("Equals", "string"), ("Ref", "string"))]);

        var order = Assert.Single(model.Entities);
        Assert.Contains("EqualsName", order.Fields.Select(f => f.Name));
        Assert.DoesNotContain("Equals", order.Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Reason == DecisionReasons.ColumnWouldHideAnInheritedMember);
    }

    [Fact]
    public void ColumnNamesAreNormalizedToIdentifiers()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("order ref", "string"), ("due-date", "DateTime"))]);

        Assert.Equal(new[] { "OrderRef", "DueDate" },
            Assert.Single(model.Entities).Fields.Select(f => f.Name));
    }

    /// <summary>A column whose name cannot become an identifier is dropped with a reason, not
    /// emitted as something that will not compile.</summary>
    [Fact]
    public void AColumnNameThatCannotBeAnIdentifierIsDroppedWithAReason()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("2nd line", "string"), ("Ref", "string"))]);

        Assert.Equal(new[] { "Ref" }, Assert.Single(model.Entities).Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Reason == DecisionReasons.ColumnNameIsNotAValidIdentifier);
    }

    /// <summary>A table whose every column was dropped still needs one editable column, the same
    /// fallback DefaultFields gives a hand-added entity.</summary>
    [Fact]
    public void ATableLeftWithNoFieldsGetsTheDefaultNameField()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("Id", "int"))]);

        Assert.Equal(new[] { DefaultFields.NameField },
            Assert.Single(model.Entities).Fields.Select(f => f.Name));
    }

    [Fact]
    public void EveryKeptEntityIsRecordedAsReadFromTheData()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("Ref", "string"))]);

        Assert.Contains(model.Decisions, d =>
            d.Name == "Order"
            && d.Stage == DecisionStage.SeedExtraction
            && d.Outcome == DecisionOutcome.Kept
            && d.Reason == DecisionReasons.ReadFromYourData
            && d.Evidence!.Contains("orders.csv"));
    }

    /// <summary>Two sources whose names normalize to the same entity must not both become it --
    /// the second would silently overwrite the first's fields.</summary>
    [Fact]
    public void TwoTablesNormalizingToOneName_KeepOnlyTheFirst()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("Ref", "string")), Table("Order", ("Other", "string"))]);

        Assert.Single(model.Entities);
        Assert.Contains(model.Decisions, d => d.Reason == DecisionReasons.AlreadyNamed);
    }

    /// <summary>The declared facts a schema source hangs on a <see cref="TabularColumn"/> must
    /// reach the <see cref="CandidateField"/> AddFields creates from it -- this is the copy Task 4
    /// exists to make, and the risk it exists to guard is exactly this path, not the hand-built
    /// <see cref="CandidateField"/> constructions the mapper-level tests use.</summary>
    [Fact]
    public void FactsDeclaredOnAColumnSurviveIntoItsField()
    {
        var table = new TabularTable { Name = "Orders", SourceLabel = "orders.csv" };
        table.Columns.Add(new TabularColumn
        {
            Name = "Ref",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 50, IsNullable = false }
        });
        table.Rows.Add(new string?[] { "x" });

        var model = DataModelBuilder.Build([table]);

        var field = Assert.Single(Assert.Single(model.Entities).Fields);
        Assert.Equal("Ref", field.Name);
        Assert.Equal(50, field.Facts!.MaxLength);
        Assert.False(field.Facts.IsNullable!.Value);
    }

    /// <summary>A column that collides with its own class name is renamed (internally,
    /// <c>ColumnFate.RenamedOffItsClassName</c>) before it becomes a field. If facts were ever
    /// carried by a lookup keyed on the FIELD name rather than the column itself, this is the
    /// case that would silently come back null.</summary>
    [Fact]
    public void FactsSurviveWhenTheColumnIsRenamedOffItsClassName()
    {
        var table = new TabularTable { Name = "Products", SourceLabel = "products.csv" };
        table.Columns.Add(new TabularColumn
        {
            Name = "Product",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 100 }
        });
        table.Columns.Add(new TabularColumn { Name = "Sku", Type = "string" });
        table.Rows.Add(new string?[] { "x", "y" });

        var model = DataModelBuilder.Build([table]);

        var product = Assert.Single(model.Entities);
        var renamed = Assert.Single(product.Fields, f => f.Name == "ProductName");
        Assert.Equal(100, renamed.Facts!.MaxLength);
    }

    [Fact]
    public void Proposals_are_scanned_from_the_whole_table_not_the_seed_sample()
    {
        var table = new TabularTable { Name = "Orders" };
        table.Columns.Add(new TabularColumn { Name = "Status" });

        for (var i = 0; i < ImportedData.MaxSeedRows + 500; i++)
            table.Rows.Add([i < ImportedData.MaxSeedRows ? "Draft" : "Reversed"]);

        var model = DataModelBuilder.Build([table]);

        var status = model.Entities.Single().Fields.Single(f => f.Name == "Status");
        var proposal = status.Proposals.Single(p => p.Kind == RuleProposalKind.Status);

        // Every value past MaxSeedRows is Reversed. A scan over ImportedData would have proposed
        // a whitelist of one, and the generated app would then reject 500 rows of its own source.
        Assert.Contains("Reversed", proposal.Rule.AllowedValues);
    }
}
