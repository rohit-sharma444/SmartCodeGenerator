namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// The commands <c>save session</c> writes: those that worked and changed something, minus what
/// has been undone.
/// </summary>
/// <remarks>
/// Mirrors the journal's own undo and redo stacks entry for entry — an entry kept no lines when its
/// command failed, and is still pushed, so an undo always pops the entry it undid. Commands the
/// journal kept no entry for are held too, but marked so that an undo never pops one: no undo
/// took them back. Not capped: the journal forgets entries past 50, a saved session should not.
/// </remarks>
public sealed class SessionRecorder
{
    private sealed record Item(IReadOnlyList<string> Lines, bool Undoable);

    private readonly UndoJournal _journal;
    private readonly List<Item> _kept = [];
    private readonly Stack<Item> _undone = new();

    public SessionRecorder(UndoJournal journal)
    {
        _journal = journal;

        journal.Recorded += lines =>
        {
            _kept.Add(new Item(lines, Undoable: true));
            _undone.Clear();
        };

        // No entry behind these, so the journal's redo stack is untouched and so is this one.
        journal.KeptWithoutEntry += lines => _kept.Add(new Item(lines, Undoable: false));

        journal.Undone += () =>
        {
            var last = _kept.FindLastIndex(item => item.Undoable);
            if (last < 0)
                return;
            _undone.Push(_kept[last]);
            _kept.RemoveAt(last);
        };

        // Appended, not put back where it was: it is applied now, after anything done since.
        journal.Redone += () =>
        {
            if (_undone.Count > 0)
                _kept.Add(_undone.Pop());
        };
    }

    /// <summary>Every kept line, oldest first.</summary>
    public IReadOnlyList<string> Lines => [.. _kept.SelectMany(item => item.Lines)];

    /// <summary>True when undo is off, so <see cref="Lines"/> holds every command that kept lines,
    /// whether or not it changed anything.</summary>
    public bool UndoWasOff => !_journal.Enabled;
}
