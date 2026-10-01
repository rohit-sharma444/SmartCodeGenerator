namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// One command's part of the undo history; disposing it ends the command.
/// </summary>
/// <remarks>
/// <see cref="Keep"/> says what <c>save session</c> should write for this command if the journal
/// records it. The caller decides — only it knows whether the command succeeded, and what a
/// replayable form of it is (a typed line, the lines a script ran, a note for an interactive
/// build).
/// </remarks>
public sealed class UndoScope : IDisposable
{
    internal enum Mode { Journaled, Off, Ignored }

    private readonly UndoJournal _journal;
    private readonly Mode _mode;
    private readonly List<string> _kept = [];
    private bool _ended;

    internal UndoScope(UndoJournal journal, Mode mode)
    {
        _journal = journal;
        _mode = mode;
    }

    /// <summary>The lines a saved session should hold for this command.</summary>
    public void Keep(IEnumerable<string> lines) => _kept.AddRange(lines);

    public void Dispose()
    {
        if (_ended)
            return;
        _ended = true;

        switch (_mode)
        {
            case Mode.Journaled:
                _journal.End(_kept);
                break;
            case Mode.Off:
                _journal.RecordWhileOff(_kept);
                break;
        }
    }
}
