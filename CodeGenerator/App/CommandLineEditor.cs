using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;

namespace CodeGenerator.App;

internal sealed class CommandLineEditor
{
    private readonly List<string> _commandHistory;
    private readonly string _historyFile;
    private int _lastSuggestionCount;
    private List<CommandInfo> _suggestions = [];
    private int _suggestionIndex = -1;
    private int _historyIndex;
    private int _inputTop;

    private const string Prompt = "CodeGenerator >> ";

    public CommandLineEditor(List<string> commandHistory,
        string historyFile)
    {
        _commandHistory = commandHistory;
        _historyFile = historyFile;
    }

    public string ReadCommand()
    {
        // The raw key editor needs a real console for both key input and cursor
        // control. When stdin or stdout is redirected (piping, scripting, no
        // console) those APIs throw "handle is invalid", so fall back to plain
        // line reading and let the app be driven non-interactively.
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return ReadRedirectedLine();

        var buffer = new List<char>();
        var cursorPosition = 0;

        _inputTop = Console.CursorTop;

        _historyIndex = _commandHistory.Count;

        _lastSuggestionCount = 0;

        ResetSuggestions();

        while (true)
        {
            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    // Enter is the one branch that returns before RewriteLine runs, so the
                    // suggestion lines RewriteLine drew below the prompt must be cleared here —
                    // otherwise they linger on screen after the command is submitted.
                    ClearSuggestionLines();
                    Console.SetCursorPosition(
                        Math.Min(Prompt.Length + buffer.Count, Console.BufferWidth - 1),
                        Math.Max(0, _inputTop));
                    return CommitCommand(buffer);

                case ConsoleKey.Tab:
                    HandleTab(ref buffer, ref cursorPosition);
                    break;

                case ConsoleKey.Escape:
                    ResetSuggestions();
                    break;

                case ConsoleKey.LeftArrow:
                    cursorPosition =
                        (key.Modifiers & ConsoleModifiers.Control) != 0
                        ? FindPreviousWord(buffer, cursorPosition)
                        : Math.Max(0, cursorPosition - 1);
                    break;

                case ConsoleKey.RightArrow:
                    cursorPosition =
                        (key.Modifiers & ConsoleModifiers.Control) != 0
                        ? FindNextWord(buffer, cursorPosition)
                        : Math.Min(buffer.Count, cursorPosition + 1);
                    break;

                case ConsoleKey.Home:
                    cursorPosition = 0;
                    break;

                case ConsoleKey.End:
                    cursorPosition = buffer.Count;
                    break;

                case ConsoleKey.Backspace:
                    if (cursorPosition > 0)
                    {
                        buffer.RemoveAt(cursorPosition - 1);
                        cursorPosition--;
                    }

                    ResetSuggestions();
                    break;

                case ConsoleKey.Delete:
                    if (cursorPosition < buffer.Count)
                        buffer.RemoveAt(cursorPosition);

                    ResetSuggestions();
                    break;

                case ConsoleKey.UpArrow:
                    LoadPreviousHistory(ref buffer, ref cursorPosition);
                    break;

                case ConsoleKey.DownArrow:
                    LoadNextHistory(ref buffer, ref cursorPosition);
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursorPosition, key.KeyChar);
                        cursorPosition++;

                        //ResetSuggestions();
                    }

                    break;
            }
            var suggestions = GetCommandHints(
                    new string([.. buffer]));

            RewriteLine(new string([.. buffer]),
                cursorPosition, suggestions);
        }
    }

    /// <summary>
    /// Reads a single command from redirected input (one command per line),
    /// mirroring the history behavior of the interactive editor. Returns
    /// "exit" at end-of-stream so the caller's loop terminates cleanly.
    /// </summary>
    private string ReadRedirectedLine()
    {
        var line = Console.ReadLine();

        if (line == null)
            return "exit";

        // Echo so redirected transcripts stay readable (the prompt was already
        // written by the caller, and ReadLine consumed the input without echo).
        Console.WriteLine(line);

        var command = line.Trim();

        RecordHistory(command);

        return command;
    }

    private string CommitCommand(List<char> buffer)
    {
        var command = new string([.. buffer]);

        Console.WriteLine();

        RecordHistory(command);

        return command;
    }

    /// <summary>
    /// Appends <paramref name="command"/> to the in-memory history and the
    /// <c>.codegen_history</c> file when <see cref="ShouldRecordInHistory"/>
    /// allows it. Shared by the interactive and redirected read paths.
    /// </summary>
    private void RecordHistory(string command)
    {
        if (!ShouldRecordInHistory(_commandHistory, command))
            return;

        _commandHistory.Add(command);

        try
        {
            CommandHistoryFile.AppendLine(_historyFile, command);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; never fail a command over it.
        }
    }

    /// <summary>
    /// The history-persistence policy: blank lines, an immediate repeat of the
    /// last entry, and the transient screen-clear commands (<c>clear</c>/<c>cls</c>)
    /// are never recorded. <c>clear</c> only wipes the display, so history — both
    /// ↑/↓ recall and the on-disk file — must look as though it was never typed;
    /// otherwise ↑ after a clear would surface "clear" instead of the previous
    /// real command.
    /// </summary>
    internal static bool ShouldRecordInHistory(
        IReadOnlyList<string> history, string command)
        => !string.IsNullOrWhiteSpace(command)
           && !ClearCommand.IsClear(command)
           && (history.Count == 0 || !history[^1].Equals(command));

    private void HandleTab(
        ref List<char> buffer,
        ref int cursorPosition)
    {
        var text = new string(buffer.ToArray());

        if (_suggestions.Count == 0)
            _suggestions = [.. GetCommandHints(text)];

        if (_suggestions.Count == 0)
            return;

        _suggestionIndex++;

        if (_suggestionIndex >= _suggestions.Count)
            _suggestionIndex = 0;

        var suggestion =
            _suggestions[_suggestionIndex];

        buffer = suggestion.Command.ToList();
        cursorPosition = buffer.Count;
    }

    private static IReadOnlyList<CommandInfo> GetCommandHints(string input)
    {
        input = CommandDictionary.Normalize(input);

        if (string.IsNullOrWhiteSpace(input))
        {
            return CommandDictionary.Commands
                .DistinctBy(c => c.Command)
                .OrderBy(c => c.Command)
                .Take(10)
                .ToList();
        }

        var startsWith = CommandDictionary.Commands
            .DistinctBy(c => c.Command)
            .Where(c =>
                c.Command.StartsWith(
                    input,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command)
            .ToList();

        if (startsWith.Count > 0)
            return startsWith;

        return CommandDictionary.Commands
            .DistinctBy(c => c.Command)
            .Where(c =>
                c.Command.Contains(
                    input,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command)
            .Take(10)
            .ToList();
    }

    private void RewriteLine(string text, int cursorPosition,
        IReadOnlyList<CommandInfo> suggestions)
    {
        const string prompt = "CodeGenerator >> ";

        int inputTop = _inputTop;

        if (inputTop < 0)
            inputTop = 0;

        var visibleSuggestions = Math.Min(5, suggestions.Count);

        // Ensure enough buffer space
        var requiredHeight = inputTop + visibleSuggestions + 2;

        if (requiredHeight >= Console.BufferHeight && OperatingSystem.IsWindows())
            Console.BufferHeight = requiredHeight + 20;

        // Clear input line
        Console.SetCursorPosition(0, inputTop);

        Console.Write(new string(' ', Console.BufferWidth - 1));

        // Clear previous suggestions
        ClearSuggestionLines();

        // Draw prompt + text
        Console.SetCursorPosition(0, inputTop);

        Console.Write(prompt);
        Console.Write(text);

        // Draw suggestions
        if (visibleSuggestions > 0)
        {
            Console.ForegroundColor =
                ConsoleColor.DarkGray;

            for (int i = 0; i < visibleSuggestions; i++)
            {
                var suggestion = suggestions[i];

                Console.SetCursorPosition(
                    0,
                    inputTop + i + 1);

                Console.Write(
                    $"  {suggestion.Command,-40} " +
                    $"{suggestion.Description}");
            }

            Console.ResetColor();
        }

        _lastSuggestionCount =
            visibleSuggestions;

        // Restore cursor
        var cursorLeft =
            Math.Min(
                prompt.Length + cursorPosition,
                Console.BufferWidth - 1);

        Console.SetCursorPosition(
            cursorLeft,
            inputTop);
    }

    private void LoadPreviousHistory(ref List<char> buffer, ref int cursorPosition)
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Max(0, _historyIndex - 1);
        buffer = [.. _commandHistory[_historyIndex]];
        cursorPosition = buffer.Count;
        ResetSuggestions();
    }

    private void LoadNextHistory(ref List<char> buffer, ref int cursorPosition)
    {
        if (_commandHistory.Count == 0)
            return;

        _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);

        var text = _historyIndex == _commandHistory.Count
            ? string.Empty
            : _commandHistory[_historyIndex];

        buffer = [.. text];
        cursorPosition = buffer.Count;
        ResetSuggestions();
    }

    private void ResetSuggestions()
    {
        _suggestions.Clear();
        _suggestionIndex = -1;
    }

    /// <summary>
    /// Blanks the suggestion lines currently drawn below the input line and forgets their count.
    /// Shared by <see cref="RewriteLine"/> (which redraws afterwards) and the Enter path (which
    /// returns without redrawing, so the suggestions would otherwise stay on screen).
    /// </summary>
    private void ClearSuggestionLines()
    {
        var inputTop = Math.Max(0, _inputTop);

        for (int i = 1; i <= _lastSuggestionCount; i++)
        {
            var line = inputTop + i;

            if (line >= Console.BufferHeight)
                break;

            Console.SetCursorPosition(0, line);
            Console.Write(new string(' ', Console.BufferWidth - 1));
        }

        _lastSuggestionCount = 0;
    }

    private static int FindPreviousWord(
        IReadOnlyList<char> buffer,
        int position)
    {
        position--;

        while (position > 0 &&
               char.IsWhiteSpace(buffer[position]))
            position--;

        while (position > 0 &&
               !char.IsWhiteSpace(buffer[position - 1]))
            position--;

        return Math.Max(0, position);
    }

    private static int FindNextWord(
        IReadOnlyList<char> buffer,
        int position)
    {
        while (position < buffer.Count &&
               !char.IsWhiteSpace(buffer[position]))
            position++;

        while (position < buffer.Count &&
               char.IsWhiteSpace(buffer[position]))
            position++;

        return position;
    }
}