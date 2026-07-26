using CodeGenerationEngine.Core;
using DomainDiscovery.Enrichment;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// One corpus, both heuristics. `create class` uses FieldTypeHeuristics and
/// `build app:` uses TypeInference; they are separate classes because
/// CodeGenerationEngine (a leaf with no project references) and DomainDiscovery
/// share no reference. This is the only test project that sees both, so it is the
/// only place the two can be held to the same answers.
/// </summary>
[TestClass]
public sealed class FieldTypeConformanceTests
{
    [TestMethod]
    [DataRow("Discount", "decimal")]
    [DataRow("Price", "decimal")]
    [DataRow("TotalCost", "decimal")]
    [DataRow("LateFine", "decimal")]
    [DataRow("Vat", "decimal")]
    [DataRow("DueDate", "DateTime")]
    [DataRow("CreatedTime", "DateTime")]
    [DataRow("CreatedAt", "DateTime")]
    [DataRow("Quantity", "int")]
    [DataRow("StockCount", "int")]
    [DataRow("Number", "int")]
    [DataRow("CustomerId", "int")]
    [DataRow("Id", "int")]
    [DataRow("IsActive", "bool")]
    [DataRow("HasLateFee", "bool")]
    [DataRow("RowGuid", "Guid")]
    [DataRow("Isbn", "string")]
    [DataRow("Hash", "string")]
    [DataRow("Fluid", "string")]
    [DataRow("Duplicate", "string")]
    [DataRow("InvoiceNumber", "string")]
    [DataRow("Title", "string")]
    [DataRow("Name", "string")]
    public void Both_heuristics_agree(string fieldName, string expected)
    {
        Assert.AreEqual(expected, FieldTypeHeuristics.Infer(fieldName),
            $"FieldTypeHeuristics disagrees for '{fieldName}'.");
        Assert.AreEqual(expected, TypeInference.Infer(fieldName),
            $"TypeInference disagrees for '{fieldName}'. The two tables must stay in step.");
    }
}
