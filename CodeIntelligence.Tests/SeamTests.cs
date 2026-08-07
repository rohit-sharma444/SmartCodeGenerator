using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using NaturalLanguageBridge;
using Xunit;

namespace CodeIntelligence.Tests;

/// <summary>Records every call so a test can assert the resolver was never reached.</summary>
public sealed class SpyResolver : IIntentResolver
{
    public List<string> Calls { get; } = [];
    public AiResolution? Next { get; init; }

    public AiResolution? Resolve(string input, CancellationToken ct)
    {
        Calls.Add(input);
        return Next;
    }
}

public sealed class SeamTests : IDisposable
{
    /// <summary>An EMPTY workspace of this test's own.
    ///
    /// The obvious choice, Path.GetTempPath(), is wrong three ways: handlers behind
    /// "validate project", "compile project" and "Order inherits AuditableBase"
    /// enumerate the workspace, so on a real machine they walk the whole Windows temp
    /// folder — slow, and it throws UnauthorizedAccessException the moment one entry
    /// is unreadable (msdtadmin here). They can also WRITE there. And the invariant
    /// under test — that a recognized command never reaches the resolver — has nothing
    /// to do with what the workspace contains.</summary>
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "ci-seam-" + Guid.NewGuid().ToString("N"));

    public SeamTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AiResolution Resolution(IntentType intent) =>
        new(intent, 0.9, [new RetrievedExample(1, "list classes", intent, 0.9)], "test");

    /// <summary>Every phrasing the rule-based path already recognizes today.</summary>
    public static TheoryData<string> RecognizedCommands()
    {
        var data = new TheoryData<string>();

        // ResetLearning phrasings are excluded deliberately: RunDetailed EXECUTES the
        // command, and "reset learning" would wipe the learned-intent store as a side
        // effect of an assertion about the resolver never being called. The invariant
        // is unaffected — these still route through the literal table, never the AI.
        foreach (var intent in LiteralCommandTable.Intents.Where(i => i != IntentType.ResetLearning))
            foreach (var phrase in LiteralCommandTable.PhrasesFor(intent))
                data.Add(phrase);

        data.Add("list classes in MyProject");
        data.Add("use project MyProject");
        data.Add("Order inherits AuditableBase");
        data.Add("rename property Name to Title in Customer");
        data.Add("add business validation Email must not be blank in Customer");
        data.Add("generate validator for Customer");

        return data;
    }

    [Theory]
    [MemberData(nameof(RecognizedCommands))]
    public void The_resolver_is_never_consulted_for_a_recognized_command(string input)
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            input, ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void Unrecognized_input_consults_the_resolver()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = null };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.Single(spy.Calls);
    }

    [Fact]
    public void Declining_the_confirmation_leaves_the_command_unrecognized()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => false);

        Assert.False(result.Recognized);
    }

    [Fact]
    public void Accepting_the_confirmation_marks_the_command_recognized()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListProjects) };
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.True(result.Recognized);
    }

    [Fact]
    public void With_no_resolver_unrecognized_input_stays_unrecognized()
    {
        IntentRepository.Initialize();
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { });

        Assert.False(result.Recognized);
    }

    [Fact]
    public void A_resolver_without_a_confirm_delegate_is_never_consulted()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: null);

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void A_resolution_of_Unknown_is_never_offered_for_confirmation()
    {
        // A resolver is contractually allowed to return non-null; nothing stops it
        // returning Unknown. Confirming that would ask the user to approve "I don't
        // know", and accepting would then mark the command Recognized while executing
        // nothing.
        IntentRepository.Initialize();

        var confirmations = 0;
        var spy = new SpyResolver { Next = Resolution(IntentType.Unknown) };
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy,
            confirmAi: _ => { confirmations++; return true; });

        Assert.Equal(0, confirmations);
        Assert.False(result.Recognized);
    }

    [Fact]
    public void The_resolvers_own_resolution_is_what_reaches_the_confirm_delegate()
    {
        // Asserting `received.Confidence == 0.77` here would be tautological — it only
        // reads back the value this test constructed. Assert.Same is the real property:
        // the seam hands the confirm delegate the resolver's OWN object, so what the
        // user is shown (and what the decision log records) cannot drift from what
        // retrieval actually decided.
        //
        // ParsedCommand.Confidence, which the seam also copies across, is deliberately
        // NOT asserted: RunDetailed returns only RunResult(Handled, Recognized), so the
        // command is not observable from out here. Task 9 owns that surface.
        IntentRepository.Initialize();
        ProjectMetaData? project = null;

        var spy = new SpyResolver
        {
            Next = new AiResolution(
                IntentType.ListProjects, 0.77,
                [new RetrievedExample(1, "list projects", IntentType.ListProjects, 0.77)], "test")
        };

        AiResolution? received = null;

        CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], _workspace, _ => { },
            onAnalyzed: null, aiResolver: spy,
            confirmAi: r => { received = r; return true; });

        Assert.Same(spy.Next, received);
    }
}
