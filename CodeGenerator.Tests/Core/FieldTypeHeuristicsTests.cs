using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class FieldTypeHeuristicsTests
{
    [TestMethod]
    // Cases the old substring matching got wrong.
    [DataRow("Discount", "decimal")]   // was int: "discount".Contains("count")
    [DataRow("Isbn", "string")]        // was bool: StartsWith("is")
    [DataRow("Issue", "string")]       // was bool
    [DataRow("Hash", "string")]        // was bool: StartsWith("has")
    [DataRow("Vat", "decimal")]        // was DateTime: EndsWith("at")
    [DataRow("Fluid", "string")]       // was Guid: Contains("uid")
    [DataRow("InvoiceNumber", "string")] // was int: an identifier code, not a count
    [DataRow("Duplicate", "string")]   // "rate" is a substring, not a token
    // Cases that must keep working.
    [DataRow("Price", "decimal")]
    [DataRow("TotalCost", "decimal")]
    [DataRow("IsActive", "bool")]
    [DataRow("HasLateFee", "bool")]    // bool prefix beats the Fee -> decimal token
    [DataRow("CreatedAt", "DateTime")]
    [DataRow("DueDate", "DateTime")]
    [DataRow("StockCount", "int")]
    [DataRow("RowGuid", "Guid")]
    [DataRow("Title", "string")]
    // Deliberate calls from the spec.
    [DataRow("Number", "int")]         // the bare name is a count
    [DataRow("CustomerId", "int")]     // ForeignKeys.cs requires int to see an FK
    [DataRow("Id", "int")]
    public void Infer_matches_whole_tokens(string fieldName, string expected)
    {
        Assert.AreEqual(expected, FieldTypeHeuristics.Infer(fieldName));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void Infer_defaults_to_string_for_no_name(string? fieldName)
    {
        Assert.AreEqual("string", FieldTypeHeuristics.Infer(fieldName!));
    }
}
