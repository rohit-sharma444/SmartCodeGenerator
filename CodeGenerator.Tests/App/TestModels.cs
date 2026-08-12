using AppScaffolding;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>Minimal fixtures for the scaffolder tests. One place, so a change to how a
/// CandidateDomainModel is built does not have to be made in five test files.</summary>
internal static class TestModels
{
    /// <summary>A model with a single entity carrying a Name field.</summary>
    public static CandidateDomainModel OneEntity(string name = "Order")
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = name,
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        return model;
    }

    /// <summary>ImportedData carrying one row for <paramref name="entityName"/>. Copy the
    /// construction from CodeGenerator.Tests/App/SeedFileWriterTests.cs.</summary>
    public static ImportedData OneImportedRow(string entityName)
        => new(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                [entityName] = new List<IReadOnlyDictionary<string, string?>>
                {
                    new Dictionary<string, string?> { ["Name"] = "Sample" },
                },
            },
            []);
}
