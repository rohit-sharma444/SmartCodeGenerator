using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Abstractions.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

public class FreeTextSeedExtractionTests
{
    [Fact]
    public void Extract_FromFreeText_PayrollApplication_YieldsSeedEntities()
    {
        // Real NLE analysis + seed extraction: the free-form description
        // must no longer degrade to a 0-entity model.
        ISemanticAnalyzer analyzer = EngineBuilder.BuildDefault().Analyzer;
        var analysis = analyzer.Analyze("payroll application");

        var model = new SeedEntityExtractor().Extract(analysis);

        Assert.NotEmpty(model.Entities);
        Assert.Contains(model.Entities, e => e.Name == "Payroll");
    }
}
