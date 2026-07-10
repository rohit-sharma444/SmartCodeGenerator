using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpCommandMapperCoverageTests
{
    [TestMethod]
    public void Mapper_reaches_every_gate_intent()
    {
        var misses = new List<string>();
        var unreachedIntents = new HashSet<IntentType>();

        foreach (var (phrase, expected) in IntentCorpus.GateEntries)
        {
            var analysis = NlpActionProvider.Analyze(phrase);
            var actual = NlpCommandMapper.ResolveIntent(analysis);

            if (actual != expected)
            {
                misses.Add($"  \"{phrase}\"  expected {expected}, got {actual}");
                unreachedIntents.Add(expected);
            }
        }

        if (misses.Count > 0)
        {
            var report =
                $"{misses.Count}/{IntentCorpus.GateEntries.Count} phrases misresolved.\n" +
                $"Unreached gate intents ({unreachedIntents.Count}): " +
                string.Join(", ", unreachedIntents.OrderBy(i => i.ToString())) + "\n" +
                string.Join("\n", misses);
            Assert.Fail(report);
        }
    }
}
