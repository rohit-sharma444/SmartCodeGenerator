namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// The commands <c>save session</c> writes: those that worked and changed something, minus what
/// has been undone.
/// </summary>
/// <remarks>
/// Mirrors the journal's own undo and redo stacks entry for entry — an entry kept no lines when its
/// command failed, and is still pushed, so an undo always pops the entry it undid. Not capped: the
/// journal forgets entries past 50, a saved session should not.
/// </remarks>
public sealed class SessionRecorder
{
    private readonly UndoJournal _journal;
    private readonly List<IReadOnlyList<string>> _kept = [];
    private readonly Stack<IReadOnlyList<string>> _undone = new();

    public SessionRecorder(UndoJournal journal)
    {
        _journal = journal;

        journal.Recorded += lines =>
        {
            _kept.Add(lines);
            _undone.Clear();
        };

        journal.Undone += () =>
        {
            if (_kept.Count == 0)
                return;
            _undone.Push(_kept[^1]);
            _kept.RemoveAt(_kept.Count - 1);
        };

        journal.Redone += () =>
        {
            if (_undone.Count > 0)
                _kept.Add(_undone.Pop());
        };
    }

    /// <summary>Every kept line, oldest first.</summary>
    public IReadOnlyList<string> Lines => [.. _kept.SelectMany(lines => lines)];

    /// <summary>True when undo is off, so <see cref="Lines"/> holds every command that kept lines,
    /// whether or not it changed anything.</summary>
    public bool UndoWasOff => !_journal.Enabled;
}
