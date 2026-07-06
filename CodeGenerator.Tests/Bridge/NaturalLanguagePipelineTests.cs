using CodeGenerationEngine.Models;
using NaturalLanguageBridge;
using NaturalLanguageEngine.Models.Semantics;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NaturalLanguagePipelineTests
{
    [TestMethod]
    public void Handle_CreateClassCommand_RunsNleThenGenerates()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(), "nlbridge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ProjectMetaData? project = null;
            SemanticResult? captured = null;
            var pipeline = new NaturalLanguagePipeline();

            var outcome = pipeline.Handle(
                "create a Customer class with Name string",
                ref project,
                new List<PropertyRule>(),
                tempRoot,
                _ => { },
                onAnalyzed: a => captured = a);

            // NLE ran and its analysis was surfaced before generation.
            Assert.IsNotNull(outcome.Analysis);
            Assert.AreSame(outcome.Analysis, captured);
            Assert.AreEqual("create", NlpActionProvider.GetPrimaryAction(outcome.Analysis));

            // The command was handled (code generated) end-to-end.
            Assert.IsTrue(outcome.Handled);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
