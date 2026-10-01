using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.App.Scripts;
using CodeIntelligence.Abstractions;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.App.Scripts;

// A script replays without the AI, so a line only the AI could read must not be saved as a
// command the replay will then fail on.
[TestClass]
[DoNotParallelize]
public sealed class ReplayableFormTests
{
    private sealed class Reads(IntentType intent) : IIntentResolver
    {
        public AiResolution? Resolve(string input, CancellationToken ct) => new(intent, 0.9, [], null);
    }

    private static PipelineOutcome Handle(string input, IIntentResolver? resolver)
    {
        var root = Path.Combine(Path.GetTempPath(), "cg_ai_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            ProjectMetaData? project = null;
            return new NaturalLanguagePipeline().Handle(input, ref project, [], root,
                p => ProjectManager.CurrentProject = p,
                aiResolver: resolver, confirmAi: resolver is null ? null : _ => true);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void The_pipeline_says_when_the_ai_read_the_line()
    {
        Assert.IsTrue(Handle("could you show me what projects there are", new Reads(IntentType.ListProjects)).NeededAi);
        Assert.IsFalse(Handle("list projects", new Reads(IntentType.ListProjects)).NeededAi);
        Assert.IsFalse(Handle("could you show me what projects there are", null).NeededAi);
    }

    [TestMethod]
    public void A_line_the_ai_read_is_saved_as_a_note_and_one_the_rules_read_as_typed()
    {
        Assert.AreEqual("add property Pages int to Book", ScriptNotes.SavedForm("add property Pages int to Book", neededAi: false));
        Assert.AreEqual(
            "# give Book a page count (understood with the AI's help, which a script does not use; write it as a command to replay it)",
            ScriptNotes.SavedForm("give Book a page count", neededAi: true));
    }

    [TestMethod]
    public void A_relationship_the_offer_set_is_saved_as_a_note_naming_what_it_added()
    {
        Assert.AreEqual(
            "# relationship from the offer, not replayed: Loan -> Book, one to many; added Loan.BookId, Loan.Book, Book.Loans",
            ScriptNotes.Relationship("Loan", "Book", "one to many", ["Loan.BookId", "Loan.Book", "Book.Loans"]));
    }
}
