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
        or CommandKind.Demo or CommandKind.ShowRelationships or CommandKind.Run;

    // Runs outside both surfaces' command try/catch; a file it cannot even read (another
    // program holds it without sharing) is a refusal, not a crash.
    public static UndoResult Handle(UndoJournal journal, CommandKind kind)
    {
        try
        {
            return kind == CommandKind.Redo ? journal.Redo() : journal.Undo();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UndoResult(false, $"Can't {(kind == CommandKind.Redo ? "redo" : "undo")}: {ex.Message}");
        }
    }

    private static bool Is(string? input, string word) =>
        (input ?? string.Empty).Trim().Equals(word, StringComparison.OrdinalIgnoreCase);
}
