using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// One message in, one turn out.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> for two reasons that both bite: <c>OutputCapture</c>
/// swaps <c>Console.Out</c>, which is process-global and refuses to nest, and every command runs
/// against the static <c>ProjectManager</c>.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatSessionTests
{
    [TestMethod]
    public async Task A_command_that_worked_reads_as_succeeded()
    {
        var session = NewSession(out _);

        await session.Send("help");

        Assert.HasCount(1, session.Turns);
        Assert.AreEqual(ChatVerdict.Succeeded, session.Turns[0].Verdict);
        Assert.AreEqual(CommandKind.Help, session.Turns[0].Kind);
        Assert.IsNotEmpty(session.Turns[0].Blocks);
    }

    [TestMethod]
    public async Task Words_the_tool_does_not_know_read_as_unrecognized()
    {
        var session = NewSession(out _);

        await session.Send("wibble the frobnicator");

        Assert.AreEqual(ChatVerdict.Unrecognized, session.Turns[0].Verdict);
    }

    [TestMethod]
    public async Task The_input_is_kept_exactly_as_it_was_typed()
    {
        var session = NewSession(out _);

        await session.Send("  Show Class Order  ");

        Assert.AreEqual("  Show Class Order  ", session.Turns[0].Input);
    }

    [TestMethod]
    public async Task Build_app_batch_and_demo_are_declined_and_never_attempted()
    {
        var session = NewSession(out _);

        foreach (var input in new[] { "build app", "build app: a shop", "batch", "demo" })
        {
            await session.Send(input);

            var turn = session.Turns[^1];

            Assert.AreEqual(ChatVerdict.Declined, turn.Verdict, $"{input} should be declined");
            Assert.IsInstanceOfType<TextBlock>(turn.Blocks[0]);
            StringAssert.Contains(
                ((TextBlock)turn.Blocks[0]).Text, "console",
                $"the decline for {input} must say where to run it");
        }
    }

    [TestMethod]
    public async Task Chat_inside_the_chat_is_declined()
    {
        var session = NewSession(out _);

        await session.Send("chat");

        Assert.AreEqual(ChatVerdict.Declined, session.Turns[0].Verdict);
    }

    [TestMethod]
    public async Task Exit_ends_the_session_without_leaving_a_turn()
    {
        var session = NewSession(out _);

        await session.Send("exit");

        Assert.IsTrue(session.Ended);
        Assert.IsEmpty(session.Turns);
    }

    [TestMethod]
    public async Task Clear_empties_the_transcript_but_not_the_history()
    {
        var session = NewSession(out _);

        await session.Send("help");
        await session.Send("clear");

        Assert.IsEmpty(session.Turns);
        Assert.Contains("help", session.History);
    }

    [TestMethod]
    public async Task A_command_is_recorded_in_history_and_clear_is_not()
    {
        var session = NewSession(out _);

        await session.Send("help");
        await session.Send("clear");
        await session.Send("cls");

        Assert.HasCount(1, session.History);
        Assert.AreEqual("help", session.History[0]);
    }

    [TestMethod]
    public async Task Every_send_raises_changed_so_both_tabs_repaint()
    {
        var session = NewSession(out _);
        var raised = 0;

        session.Changed += () => raised++;

        await session.Send("help");

        Assert.IsGreaterThan(0, raised);
    }

    [TestMethod]
    public async Task A_command_that_throws_becomes_a_failed_turn_rather_than_a_dead_session()
    {
        // Nothing in the catalog is guaranteed to throw, so this asserts the property that
        // matters: whatever happens, Console.Out comes back and the session still works.
        var session = NewSession(out _);
        var before = Console.Out;

        await session.Send("show class \0illegal");

        Assert.AreSame(before, Console.Out);
        Assert.HasCount(1, session.Turns);

        await session.Send("help");

        Assert.HasCount(2, session.Turns);
    }

    [TestMethod]
    public void Loading_a_folder_records_a_turn_that_names_the_new_root()
    {
        var session = NewSession(out _);
        var other = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(other, "Api"));
        File.WriteAllText(
            Path.Combine(other, "Api", "Api.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"></Project>""");

        var load = session.Load(other);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(other, session.Root);
        Assert.HasCount(1, session.Turns);
        StringAssert.Contains(((TextBlock)session.Turns[0].Blocks[0]).Text, other);
    }

    [TestMethod]
    public void Loading_a_path_that_is_not_there_reports_it_and_keeps_the_root()
    {
        var session = NewSession(out var workspace);
        var before = workspace.Root;

        var load = session.Load(Path.Combine(before, "nowhere-at-all"));

        Assert.IsFalse(load.Ok);
        Assert.AreEqual(before, session.Root);
        Assert.AreEqual(ChatVerdict.Failed, session.Turns[0].Verdict);
    }

    private static ChatSession NewSession(out Workspace workspace)
    {
        var root = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        workspace = new Workspace(root, projectPathSet: false);

        return new ChatSession(workspace, AiLayer.Create(), []);
    }
}
