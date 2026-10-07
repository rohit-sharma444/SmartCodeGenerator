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

        var progress = new List<DiscoveryProgress>();

        var outcome = new OllamaDomainModelSource(new OllamaClient(config), config.Model)
            .Discover("A point of sale system for a retail shop", progress.Add);

        Assert.True(outcome.Succeeded, outcome.Message);

        // The progress callback against the REAL model, which is the only place it can be proven:
        // a stub can be made to report anything. A generation takes minutes on a CPU, and this is
        // what the Discover panel draws instead of a blank wait for all of it.
        Assert.NotEmpty(progress);

        // Reported as it goes, not once at the end - the first report must know less than the
        // last, or nothing was actually incremental.
        Assert.True(
            progress[0].Total < progress[^1].Total || progress.Count == 1,
            $"progress went straight to {progress[0].Total} classes in one step");

        Assert.All(progress, p => Assert.All(
            p.Done.Concat(p.Pending),
            line => Assert.False(string.IsNullOrWhiteSpace(line), "a progress line was blank")));

        // The two-phase route's whole promise: at some point the complete class list is known and
        // none of it is finished yet. Proven against the real model, since only a real roster call
        // can show that the split actually happens.
        Assert.Contains(progress, p => p.Done.Count == 0 && p.Pending.Count > 1);

        // And by the end every class the roster named has been accounted for.
        Assert.Equal(outcome.Model.Entities.Count, progress[^1].Total);

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

            // The rule a 3B model reproduces the mistakes of: a place or a machine holding ONE
            // person (CashRegister.CustomerId). Any other key the model declared is a real
            // "belongs to" (POSTerminal.StoreId) - LlmModelNormalizer.MayCarryAKeyTo.
            if (entity.Role is DomainDiscovery.Enrichment.EntityRole.Place or DomainDiscovery.Enrichment.EntityRole.Device)
                foreach (var relationship in entity.Relationships)
                    Assert.False(
                        outcome.Model.Entities.Any(e => e.Name == relationship.Target
                            && e.Role == DomainDiscovery.Enrichment.EntityRole.Party),
                        $"'{entity.Name}' ({entity.Role}) must not hold one person ({relationship.Target}).");
        }
    }
}
