using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class UndoCommandTests
{
    [TestMethod]
    [DataRow(CommandKind.Pipeline, true)]
    [DataRow(CommandKind.BuildApp, true)]
    [DataRow(CommandKind.Batch, true)]
    [DataRow(CommandKind.Demo, true)]
    [DataRow(CommandKind.ShowRelationships, true)]
    [DataRow(CommandKind.Status, false)]
    [DataRow(CommandKind.Help, false)]
    [DataRow(CommandKind.History, false)]
    [DataRow(CommandKind.Clear, false)]
    [DataRow(CommandKind.Exit, false)]
    [DataRow(CommandKind.Undo, false)]
    [DataRow(CommandKind.Redo, false)]
    [DataRow(CommandKind.Chat, false)]
    [DataRow(CommandKind.Ai, false)]
    public void Only_commands_that_can_change_something_are_journalled(CommandKind kind, bool journaled)
        => Assert.AreEqual(journaled, UndoCommand.Journaled(kind));
}
