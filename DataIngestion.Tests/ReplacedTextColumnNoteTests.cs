using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// A text column named for a table the same row also links to by key. The generated class keeps
/// the link and drops the text (DomainModelMapper.DropTextShadowingTheRelationship), and its
/// values went with it without a word.
/// </summary>
public class ReplacedTextColumnNoteTests
{
    /// <summary>Customers(CustomersId, Name) and Shipment(Id, Customer nvarchar, CustomersId FK),
    /// as SQL Server reads them.</summary>
    private static TabularTable[] Tables()
    {
        var customers = new TabularTable { Name = "Customers" };
        customers.Columns.Add(new TabularColumn { Name = "CustomersId", Type = "int" });
        customers.Columns.Add(new TabularColumn { Name = "Name", Type = "string" });
        customers.Rows.Add(["1", "Ada"]);

        var shipment = new TabularTable { Name = "Shipment" };
        shipment.Columns.Add(new TabularColumn { Name = "Id", Type = "int" });
        shipment.Columns.Add(new TabularColumn { Name = "Customer", Type = "string" });
        shipment.Columns.Add(new TabularColumn { Name = "CustomersId", Type = "int" });
        shipment.ForeignKeys.Add(new TabularForeignKey("CustomersId", "Customers", "CustomersId"));
        shipment.Rows.Add(["1", "Ada Lovelace", "1"]);

        return [customers, shipment];
    }

    [Fact]
    public void A_text_column_replaced_by_the_link_is_reported_in_one_line()
    {
        var model = DataModelBuilder.Build(Tables());

        Assert.Equal(
            ["Shipment.Customer (text) was replaced by the link to Customer; its values were not kept."],
            DataModelBuilder.ReplacedTextNotes(model));
    }

    /// <summary>The note is only as true as the drop: the generated class really has no text
    /// Customer, and does have the key and the link.</summary>
    [Fact]
    public void The_generated_class_has_the_link_and_not_the_text()
    {
        var classes = AppScaffolding.DomainModelMapper.ToClassDefinitions(DataModelBuilder.Build(Tables()));
        var shipment = classes.Single(c => c.Name == "Shipment");

        Assert.Contains(shipment.Properties, p => p.Name == "CustomerId" && p.Type == "int");
        Assert.DoesNotContain(shipment.Properties, p => p.Name == "Customer" && p.Type == "string");
        Assert.Contains(shipment.Properties, p => p.Name == "Customer");
    }

    [Fact]
    public void A_text_column_with_no_link_beside_it_is_not_reported()
    {
        var tables = Tables();
        tables[1].ForeignKeys.Clear();
        tables[1].Columns[2] = new TabularColumn { Name = "Weight", Type = "int" };

        Assert.Empty(DataModelBuilder.ReplacedTextNotes(DataModelBuilder.Build(tables)));
    }
}
