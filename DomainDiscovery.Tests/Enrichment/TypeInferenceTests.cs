using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class TypeInferenceTests
{
    [Theory]
    [InlineData("Price", "decimal")]
    [InlineData("TotalCost", "decimal")]
    [InlineData("LateFine", "decimal")]
    [InlineData("DueDate", "DateTime")]
    [InlineData("CreatedTime", "DateTime")]
    [InlineData("Quantity", "int")]
    [InlineData("StockCount", "int")]
    [InlineData("IsActive", "bool")]
    [InlineData("HasLateFee", "bool")]
    [InlineData("Title", "string")]
    [InlineData("Name", "string")]
    [InlineData("Discount", "decimal")]
    [InlineData("Isbn", "string")]
    [InlineData("Hash", "string")]
    [InlineData("Vat", "decimal")]
    [InlineData("InvoiceNumber", "string")]
    [InlineData("Number", "int")]
    [InlineData("CustomerId", "int")]
    [InlineData("CreatedAt", "DateTime")]
    [InlineData("RowGuid", "Guid")]
    public void Infer_ReturnsExpectedType(string fieldName, string expected)
    {
        Assert.Equal(expected, TypeInference.Infer(fieldName));
    }
}
