using CodeGenerationEngine.Models;
using CodeGenerator.App;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Storage;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The confirmation prompt's decidable half, and the decision-log wiring behind it.
///
/// What is NOT covered here is deliberate and matches how <c>SelectionPrompt</c> is
/// treated elsewhere in this project: painting the menu and reading arrow keys needs a
/// real console, which a test cannot supply. Everything a test CAN reach — the wording,
/// the default, what each answer means, and what gets logged — is reached.
/// </summary>
[TestClass]
public class AiConfirmationTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ai-confirm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private DecisionLog Log() => new(Path.Combine(_dir, "ai-index.db"));

    private static AiResolution Resolution(
        IntentType intent = IntentType.ListClasses, double confidence = 0.82) =>
        new(intent, confidence,
            [new RetrievedExample(7, "list classes", intent, 0.9)], "because");

    [TestMethod]
    public void The_title_names_the_reading_its_confidence_and_the_closest_example()
    {
        var title = AiConfirmPrompt.Title(Resolution());

        StringAssert.Contains(title, "ListClasses");
        StringAssert.Contains(title, "82%");
        StringAssert.Contains(title, "list classes");
    }

    [TestMethod]
    public void The_title_survives_a_resolution_with_no_evidence()
    {
        // Evidence is a list and nothing in the contract forbids it being empty;
        // indexing [0] blind would crash the REPL at the prompt.
        var title = AiConfirmPrompt.Title(
            new AiResolution(IntentType.ListClasses, 0.6, [], null));

        StringAssert.Contains(title, "(none)");
    }

    [TestMethod]
    public void Only_the_yes_row_accepts()
    {
        Assert.IsTrue(AiConfirmPrompt.Accepted(0));
        Assert.IsFalse(AiConfirmPrompt.Accepted(1));
    }

    [TestMethod]
    public void Cancelling_the_menu_declines_rather_than_running()
    {
        // Choose returns null on Esc or end-of-input. Anything but an explicit yes must
        // decline, because accepting could execute a code-mutating command.
        Assert.IsFalse(AiConfirmPrompt.Accepted(null));
    }

    [TestMethod]
    public void The_default_row_is_no()
    {
        Assert.AreEqual("No, leave it", AiConfirmPrompt.Options[AiConfirmPrompt.DefaultIndex]);
        Assert.IsFalse(AiConfirmPrompt.Accepted(AiConfirmPrompt.DefaultIndex));
    }

    [TestMethod]
    public void Accepting_records_the_decision_as_accepted()
    {
        var log = Log();

        var accepted = AiDecisionRecorder.ConfirmAndRecord(
            Resolution(), "show me every class", log, _ => true);

        Assert.IsTrue(accepted);

        var last = log.Last();
        Assert.IsNotNull(last);
        Assert.AreEqual("show me every class", last.Input);
        Assert.AreEqual(nameof(IntentType.ListClasses), last.Intent);
        Assert.IsTrue(last.Accepted);
    }

    [TestMethod]
    public void Declining_is_recorded_too_rather_than_dropped()
    {
        // The point of the log is calibration, and a decline is as informative as an
        // accept. Recording only the yes answers would bias every future threshold.
        var log = Log();

        var accepted = AiDecisionRecorder.ConfirmAndRecord(
            Resolution(), "flurble the wotsit", log, _ => false);

        Assert.IsFalse(accepted);

        var last = log.Last();
        Assert.IsNotNull(last);
        Assert.AreEqual("flurble the wotsit", last.Input);
        Assert.IsNotNull(last.Accepted);
        Assert.IsFalse(last.Accepted);
    }

    [TestMethod]
    public void The_resolution_reaching_the_prompt_is_the_one_that_was_resolved()
    {
        AiResolution? seen = null;
        var resolution = Resolution(IntentType.ListProjects, 0.91);

        AiDecisionRecorder.ConfirmAndRecord(
            resolution, "what have i got", Log(), r => { seen = r; return false; });

        Assert.AreSame(resolution, seen);
    }
}
