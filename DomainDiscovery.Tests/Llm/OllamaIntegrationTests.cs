using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// The only test that needs a running Ollama.
/// </summary>
/// <remarks>
/// Everything else on this path is covered by unit tests over a fake client; this is the only
/// thing that proves a real model's output survives normalization. It does nothing when Ollama
/// is not answering — CI has no Ollama installed, and a red suite there would be a false alarm.
/// <para>
/// A plain <c>[Fact]</c> that returns early rather than a skippable one, following
/// <c>DataIngestion.Tests.Schema.Readers.Sql.SqlServerIntegrationTests</c>: expressing the skip
/// properly would need Xunit.SkippableFact, and this repository has already declined to add that
/// package for exactly this situation. The cost is that a machine with no Ollama reports this
/// test as passed rather than skipped; the alternative is a new dependency for one test.
/// </para>
/// </remarks>
public class OllamaIntegrationTests
{
    private static bool OllamaIsRunning(DiscoveryConfig config)
        => new OllamaClient(config).IsAvailable();

    [Fact]
    public void ProposesAModelForAPointOfSaleSystem()
    {
        var config = new DiscoveryConfig();

        if (!OllamaIsRunning(config))
            return;   // no local model server — see the class remarks

        var outcome = new OllamaDomainModelSource(new OllamaClient(config), config.Model)
            .Discover("A point of sale system for a retail shop");

        Assert.True(outcome.Succeeded, outcome.Message);

        // Deliberately weak: the model is sampled, so asserting exact class names would make
        // this test flaky. What is asserted is that the normalizer produced something usable.
        Assert.NotEmpty(outcome.Model.Entities);

        foreach (var entity in outcome.Model.Entities)
        {
            Assert.True(
                DomainDiscovery.SeedExtraction.NameNormalizer.IsValidIdentifier(entity.Name),
                $"'{entity.Name}' would not compile as a class name.");

            // A KEY, not a field literally named "Id". The default model names Sale's key
            // "SaleID", and LlmModelNormalizer.EnsureKey deliberately accepts that rather than
            // adding a second one — so asserting the exact string here would fail against the
            // real model's actual output.
            Assert.Contains(entity.Fields, f =>
                f.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || f.Name.Equals($"{entity.Name}Id", StringComparison.OrdinalIgnoreCase));

            // The rule a 3B model reproduces the mistakes of: only a Document, Transaction or
            // Event may carry a foreign key.
            if (entity.Relationships.Count > 0)
                Assert.True(
                    DomainDiscovery.Enrichment.EntityRoleRank.CanReference(entity.Role),
                    $"'{entity.Name}' ({entity.Role}) must not carry a relationship.");
        }
    }
}
