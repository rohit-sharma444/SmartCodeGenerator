using AppScaffolding;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The imported rows follow the classes they were read with, however the Review stage renamed
/// them. <c>RenamedFieldsKeepTheirDataTests</c> covers the one-rename paths end to end.
/// </summary>
[TestClass]
public class ImportedDataRenamesTests
{
    /// <summary>
    /// Two classes that trade names trade rows. Each rename used to remove the old key from the
    /// dictionary the previous one had just written to, so the second deleted the first's rows.
    /// </summary>
    [TestMethod]
    public void Two_classes_that_swap_names_swap_their_rows()
    {
        var data = new ImportedData(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["Customer"] = [Row("Harbor Cafe")],
                ["Supplier"] = [Row("Acme Parts")],
            },
            []);

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Supplier", ImportedAs = "Customer" });
        model.Entities.Add(new CandidateEntity { Name = "Customer", ImportedAs = "Supplier" });

        var followed = ImportedDataRenames.Follow(data, model);

        Assert.AreEqual("Harbor Cafe", Assert.ContainsSingle(followed.For("Supplier"))["Name"]);
        Assert.AreEqual("Acme Parts", Assert.ContainsSingle(followed.For("Customer"))["Name"]);
    }

    private static IReadOnlyDictionary<string, string?> Row(string name)
        => new Dictionary<string, string?> { ["Name"] = name };
}
