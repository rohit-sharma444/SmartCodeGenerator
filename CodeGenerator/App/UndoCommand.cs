using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.App;

/// <summary>
/// <c>undo</c> and <c>redo</c>, and which commands go into the history they work on.
/// </summary>
internal static class UndoCommand
{
    public static bool IsUndo(string? input) => Is(input, "undo");

    public static bool IsRedo(string? input) => Is(input, "redo");

    /// <summary>
    /// The kinds that can change the workspace or the session. The rest — help, status, history,
    /// clear, exit, the AI switch, starting the chat page, and undo and redo themselves — never
    /// make an entry.
    /// </summary>
    public static bool Journaled(CommandKind kind) => kind is
        CommandKind.Pipeline or CommandKind.BuildApp or CommandKind.Batch
        or CommandKind.Demo or CommandKind.ShowRelationships;

    public static UndoResult Handle(UndoJournal journal, CommandKind kind) =>
        kind == CommandKind.Redo ? journal.Redo() : journal.Undo();

    private static bool Is(string? input, string word) =>
        (input ?? string.Empty).Trim().Equals(word, StringComparison.OrdinalIgnoreCase);
}
