using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Which handler an input belongs to — asserted against the predicates that used to be an
/// if-ladder in the REPL.
/// </summary>
/// <remarks>
/// There were two ladders before this: one in <c>Run</c> and a shorter one in a different order in
/// <c>RunOnce</c>. The chat would have been a third. The order is behaviour, not tidiness, and
/// `help build app` is the case that proves it — it is a help topic, not a build.
/// </remarks>
[TestClass]
public class CommandRouteTests
{
    [TestMethod]
    public void Exit_is_the_bare_word_only()
    {
        Assert.AreEqual(CommandKind.Exit, CommandRoute.Classify("exit"));
        Assert.AreEqual(CommandKind.Exit, CommandRoute.Classify("EXIT"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("exit the building"));
    }

    [TestMethod]
    public void Help_about_build_app_is_help_not_build_app()
    {
        // The whole reason the order is fixed here. RunOnce gets this right today only because
        // IsBuildApp is anchored to the front of the input; nothing said so out loud.
        Assert.AreEqual(CommandKind.Help, CommandRoute.Classify("help build app"));
        Assert.AreEqual(CommandKind.BuildApp, CommandRoute.Classify("build app"));
        Assert.AreEqual(CommandKind.BuildApp, CommandRoute.Classify("build app: a shop"));
    }

    [TestMethod]
    public void Each_kind_agrees_with_the_predicate_it_replaced()
    {
        Assert.AreEqual(CommandKind.Clear, CommandRoute.Classify("clear"));
        Assert.AreEqual(CommandKind.Clear, CommandRoute.Classify("cls"));
        Assert.AreEqual(CommandKind.History, CommandRoute.Classify("history"));
        Assert.AreEqual(CommandKind.Help, CommandRoute.Classify("help"));
        Assert.AreEqual(CommandKind.Status, CommandRoute.Classify("status"));
        Assert.AreEqual(CommandKind.Demo, CommandRoute.Classify("demo"));
        Assert.AreEqual(CommandKind.Ai, CommandRoute.Classify("ai status"));
        Assert.AreEqual(CommandKind.Batch, CommandRoute.Classify("batch"));
    }

    [TestMethod]
    public void Chat_is_its_own_kind_so_the_page_can_decline_it()
    {
        // The chat page declines `chat` — you are already in it — and it can only decline a kind
        // classification actually produces.
        Assert.AreEqual(CommandKind.Chat, CommandRoute.Classify("chat"));
        Assert.AreEqual(CommandKind.Help, CommandRoute.Classify("help chat"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("chat about the weather"));
    }

    [TestMethod]
    public void Anything_else_is_the_pipeline()
    {
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("list classes"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("create class Employee"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("wibble"));
    }

    [TestMethod]
    public void Nothing_typed_is_the_pipeline_which_is_what_the_repl_did()
    {
        // The REPL passes empty input straight to the pipeline — the blank-input guard is
        // commented out. Classification must not invent a new behaviour here.
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify(""));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify(null));
    }

    [TestMethod]
    [DataRow("undo", CommandKind.Undo)]
    [DataRow("  Undo ", CommandKind.Undo)]
    [DataRow("redo", CommandKind.Redo)]
    [DataRow("undo delete class Book", CommandKind.Pipeline)]
    public void Undo_and_redo_are_the_bare_words(string input, CommandKind kind)
        => Assert.AreEqual(kind, CommandRoute.Classify(input));
}
