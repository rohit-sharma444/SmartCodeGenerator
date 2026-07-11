using System.Linq;
using System.Reflection;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpBoundaryTests
{
    [TestMethod]
    public void CodeGenerationEngine_no_longer_carries_the_duplicate_nlp_stack()
    {
        var asm = typeof(ParsedCommand).Assembly; // CodeGenerationEngine
        string[] retired =
        {
            "IntentAnalyzer", "SemanticAnalyzer", "TextTokenizer", "Tokenizer",
            "EnglishLanguageModel", "EntityRecognizer", "RelationExtractor",
            "SemanticIntentAnalyzer", "SemanticPipeline", "IntentLearningEngine",
            "VocabularyRepository", "VocabularyStore", "Synonyms"
        };
        var present = asm.GetTypes().Select(t => t.Name).ToHashSet();
        var leaked = retired.Where(present.Contains).ToList();
        Assert.IsEmpty(leaked, "still present: " + string.Join(", ", leaked));
    }
}
