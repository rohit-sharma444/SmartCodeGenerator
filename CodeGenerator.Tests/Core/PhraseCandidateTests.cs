using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A command the CLI did not understand, followed soon after by one it did, is recorded as a
/// candidate phrasing for that intent - the raw material for learned-phrases.tsv.
/// </summary>
[TestClass]
[DoNotParallelize] // LearningCoordinator and the candidate log are process-wide.
public sealed class PhraseCandidateTests
{
    private DateTime _now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [TestInitialize]
    public void Setup()
    {
        LearnedIntentStore.UseInMemory();
        LearningCoordinator.ResetHistory();
        LearningCoordinator.Clock = () => _now;
    }

    [TestCleanup]
    public void Cleanup() => LearningCoordinator.ResetHistory();

    private static void Miss(string sentence)
        => LearningCoordinator.RecordOutcome(sentence, IntentType.Unknown, success: false,
            new ParsedCommand { Intent = IntentType.Unknown, OriginalSentence = sentence });

    private static void Fix(string sentence, IntentType intent, bool success = true, string className = "")
        => LearningCoordinator.RecordOutcome(sentence, intent, success,
            new ParsedCommand { Intent = intent, OriginalSentence = sentence, ClassName = className });

    [TestMethod]
    public void A_miss_then_a_working_retry_records_the_phrase_without_its_arguments()
    {
        Miss("make me a new class Invoice");
        _now = _now.AddSeconds(8);
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

        var candidate = PhraseCandidates.All.Single();
        Assert.AreEqual("make me a new class", candidate.Phrase);
        Assert.AreEqual(IntentType.CreateClass, candidate.Intent);
        Assert.AreEqual("make me a new class Invoice", candidate.Miss);
        Assert.AreEqual("create class Invoice", candidate.FixedBy);
    }

    [TestMethod]
    public void A_retry_that_also_fails_teaches_nothing()
    {
        Miss("make me a new class Invoice");
        Fix("create class Invoice", IntentType.CreateClass, success: false, className: "Invoice");

        Assert.IsEmpty(PhraseCandidates.All);
    }

    /// <summary>Nothing in common, so not a retry: someone who gives up and types "help" has not
    /// said that "make me a new class" means Help.</summary>
    [TestMethod]
    public void A_following_command_with_no_word_in_common_is_not_a_retry()
    {
        Miss("make me a new class Invoice");
        Fix("show help", IntentType.Help);

        Assert.IsEmpty(PhraseCandidates.All);
    }

    [TestMethod]
    public void A_command_after_the_window_is_not_a_retry()
    {
        Miss("make me a new class Invoice");
        _now = _now.AddSeconds(LearningCoordinator.RephraseWindowSeconds + 1);
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

        Assert.IsEmpty(PhraseCandidates.All);
    }

    /// <summary>A phrase that is nothing but the argument says nothing about the command.</summary>
    [TestMethod]
    public void A_miss_that_was_only_the_argument_is_not_a_phrasing()
    {
        Miss("Invoice");
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

        Assert.IsEmpty(PhraseCandidates.All);
    }

    /// <summary>Only the most recent miss is paired: one fix is evidence about the thing it
    /// followed, not about everything typed before it.</summary>
    [TestMethod]
    public void A_retry_is_paired_with_the_miss_just_before_it_only()
    {
        Miss("make me a new class Invoice");
        Miss("add me a class Invoice");
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

        Assert.AreEqual("add me a class", PhraseCandidates.All.Single().Phrase);
    }

    /// <summary>The same phrasing from several retries is one line to review, most-seen first:
    /// how often people said it is the best evidence the reviewer has.</summary>
    [TestMethod]
    public void The_review_groups_repeats_and_puts_the_most_seen_first()
    {
        Miss("make me a new class Invoice");
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");
        Miss("list all my classes please");
        Fix("list classes", IntentType.ListClasses);
        Miss("make me a new class Customer");
        Fix("create class Customer", IntentType.CreateClass, className: "Customer");

        var review = PhraseCandidates.Review();

        Assert.HasCount(2, review);
        Assert.AreEqual("make me a new class", review[0].Phrase);
        Assert.AreEqual(2, review[0].Count);
        Assert.AreEqual(IntentType.ListClasses, review[1].Intent);
    }

    /// <summary>The review file is in learned-phrases.tsv's own format, so an approved line is
    /// copied across as it stands - checked with the parser that reads the shipped file.</summary>
    [TestMethod]
    public void The_review_file_is_in_the_shipped_files_format()
    {
        Miss("make me a new class Invoice");
        Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

        var tsv = PhraseCandidates.ReviewTsv();
        var parsed = CodeIntelligence.Corpus.LearnedPhrases.Parse(tsv);

        Assert.IsEmpty(parsed.Problems, string.Join("\n", parsed.Problems));
        Assert.AreEqual("make me a new class", parsed.Documents.Single().Text);
        Assert.AreEqual(IntentType.CreateClass, parsed.Documents.Single().Intent);
    }

    [TestMethod]
    public void Candidates_are_kept_in_the_workspace_and_read_back()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cand_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        try
        {
            LearnedIntentStore.Initialize(workspace);
            LearningCoordinator.ResetHistory();
            LearningCoordinator.Clock = () => _now;

            Miss("make me a new class Invoice");
            Fix("create class Invoice", IntentType.CreateClass, className: "Invoice");

            LearnedIntentStore.Initialize(workspace);

            Assert.IsTrue(File.Exists(Path.Combine(workspace, ".codegen", PhraseCandidates.FileName)));
            Assert.AreEqual("make me a new class", PhraseCandidates.All.Single().Phrase);
        }
        finally
        {
            LearnedIntentStore.UseInMemory();
            Directory.Delete(workspace, recursive: true);
        }
    }
}
