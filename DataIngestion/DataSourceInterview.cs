using DataIngestion.Schema.Readers;

namespace DataIngestion;

/// <summary>What the interview concluded.</summary>
public enum InterviewDecision
{
    /// <summary>The user has no data. The caller runs the unchanged Wikipedia path.</summary>
    NoData,

    /// <summary>The user named a source. <see cref="InterviewOutcome.Source"/> holds it.</summary>
    Source,

    /// <summary>
    /// The user backed out, or stdin closed mid-interview. Deliberately not
    /// <see cref="NoData"/>: someone who abandons the interview must not be handed a Wikipedia
    /// build they never asked for.
    /// </summary>
    Cancelled
}

public sealed record InterviewOutcome(InterviewDecision Decision, DataSourceSpec? Source);

/// <summary>
/// Asks whether the user has data, in what format, and where. Reads and writes through the
/// <see cref="TextReader"/>/<see cref="TextWriter"/> it is given rather than the console, so the
/// whole flow is drivable from a test — the pattern <c>BuildAppCommand.ResolveTarget</c>
/// established for exactly this reason.
/// </summary>
/// <remarks>
/// The caller decides whether to run this at all: redirected stdin never prompts. See
/// <see cref="InlineDataSource"/> for the scriptable form.
/// </remarks>
public static class DataSourceInterview
{
    private static readonly InterviewOutcome Cancelled = new(InterviewDecision.Cancelled, null);

    /// <summary>The question that decides whether a format is asked for at all.</summary>
    private const string GateTitle = "Do you have existing data?";

    /// <summary>The format the menu pre-selects.</summary>
    /// <remarks>
    /// Not the first row: <see cref="DataSourceReaders.Keys"/> is ordered as the requirements
    /// listed the formats and heads with <c>sql</c>, the one kind this build cannot read — so
    /// pre-selecting row zero would make Enter the one answer that cannot work.
    /// </remarks>
    private const string DefaultFormat = "csv";

    /// <param name="choose">
    /// Asks a question with a fixed set of answers — the CLI's drop-down, passed in because this
    /// library cannot reference it. Null falls back to the typed prompts, which is what a console
    /// that cannot paint a list gets.
    /// </param>
    /// <param name="workspaceRoot">
    /// What a relative file path is read against. Null keeps the process's current directory.
    /// </param>
    public static InterviewOutcome Resolve(
        TextReader reader, TextWriter writer,
        Func<string, IReadOnlyList<string>, int, int?>? choose = null,
        string? workspaceRoot = null)
        => choose is null
            ? ResolveTyped(reader, writer, workspaceRoot)
            : ResolveChosen(reader, writer, choose, workspaceRoot);

    private static InterviewOutcome ResolveTyped(
        TextReader reader, TextWriter writer, string? workspaceRoot)
    {
        writer.Write($"{GateTitle} [y/N]: ");
        var answer = reader.ReadLine();

        // EOF is the absence of an answer, not the default answer. Conflating the two would run a
        // whole discovery pipeline off a closed pipe.
        if (answer is null)
            return Cancelled;

        var trimmed = answer.Trim();

        if (trimmed.Length == 0
            || trimmed.StartsWith("n", StringComparison.OrdinalIgnoreCase))
            return new InterviewOutcome(InterviewDecision.NoData, null);

        return ResolveFormat(reader, writer, workspaceRoot);
    }

    /// <summary>The same two questions as a pair of menus.</summary>
    /// <remarks>
    /// Only the asking differs: an unreadable format still explains itself and asks again, and the
    /// path is still typed, because a path is not a choice.
    /// </remarks>
    private static InterviewOutcome ResolveChosen(
        TextReader reader, TextWriter writer,
        Func<string, IReadOnlyList<string>, int, int?> choose,
        string? workspaceRoot)
    {
        // No is the pre-selected row, which is the answer the typed prompt's blank line gave.
        var gate = choose(GateTitle, ["Yes", "No"], 1);

        if (gate is null)
            return Cancelled;

        if (gate != 0)
            return new InterviewOutcome(InterviewDecision.NoData, null);

        var keys = DataSourceReaders.Keys;

        var labels = keys.Select(FormatLabel).ToList();
        var fallback = Math.Max(0, keys.ToList().IndexOf(DefaultFormat));

        while (true)
        {
            var row = choose("Format", labels, fallback);

            if (row is null)
                return Cancelled;

            // Every row comes from the same catalogue the parser reads, so a miss is a wiring bug.
            if (!DataSourceReaders.TryParseKind(keys[row.Value], out var kind))
                return Cancelled;

            // On the menu, but with no reader in this build. Say why, say what works, ask again.
            if (!DataSourceReaders.TryCreate(kind, out _))
            {
                writer.WriteLine("  " + DataSourceReaders.NotSupportedMessage(kind));
                continue;
            }

            return ResolveLocation(kind, reader, writer, workspaceRoot);
        }
    }

    /// <summary>
    /// A format's row. The one kind this build cannot read says so before it is chosen rather than
    /// only after — a menu that offers something and then refuses it wastes the answer.
    /// </summary>
    private static string FormatLabel(string key)
        => DataSourceReaders.TryParseKind(key, out var kind)
           && !DataSourceReaders.TryCreate(kind, out _)
            ? $"{key} — not in this build yet"
            : key;

    private static InterviewOutcome ResolveFormat(
        TextReader reader, TextWriter writer, string? workspaceRoot)
    {
        while (true)
        {
            writer.Write($"Format [{string.Join("/", DataSourceReaders.Keys)}] (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (!DataSourceReaders.TryParseKind(text, out var kind))
            {
                writer.WriteLine(
                    "  Unknown format. Valid: " + string.Join(", ", DataSourceReaders.Keys));
                continue;
            }

            // On the menu, but with no reader in this build. Say why, say what works, ask again.
            if (!DataSourceReaders.TryCreate(kind, out _))
            {
                writer.WriteLine("  " + DataSourceReaders.NotSupportedMessage(kind));
                continue;
            }

            return ResolveLocation(kind, reader, writer, workspaceRoot);
        }
    }

    /// <summary>
    /// Where the data is. A file for every kind but SQL, whose location is a connection
    /// string.
    /// </summary>
    /// <remarks>
    /// The split exists because this asked for a "SQL file path" and then refused anything
    /// File.Exists could not find - so the console interview could offer SQL and never
    /// accept it. The two branches share the cancel and the re-ask; nothing else about them
    /// is the same, since a connection string cannot be checked without opening it.
    /// </remarks>
    private static InterviewOutcome ResolveLocation(
        DataSourceKind kind, TextReader reader, TextWriter writer, string? workspaceRoot)
        => kind == DataSourceKind.Sql
            ? ResolveConnection(reader, writer)
            : ResolveFile(kind, reader, writer, workspaceRoot);

    /// <summary>
    /// The connection string, taken as typed.
    /// </summary>
    /// <remarks>
    /// Not validated here beyond being non-empty: the only way to know a connection string
    /// works is to open it, and the discovery step already reports what the server said in
    /// terms far better than anything invented at this prompt.
    /// <para>
    /// No table is named, which means every table is read. The console interview asks its
    /// questions before anything has been read, so there is no list to choose from yet - the
    /// browser workflow is where tables are picked, and a scripted run names them with
    /// <c>tables A,B,C</c>.
    /// </para>
    /// </remarks>
    private static InterviewOutcome ResolveConnection(TextReader reader, TextWriter writer)
    {
        while (true)
        {
            writer.Write("SQL Server connection string (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (text.Length == 0)
                continue;

            var connection = Unquote(text);

            return new InterviewOutcome(
                InterviewDecision.Source,
                new DataSourceSpec(
                    DataSourceKind.Sql,
                    connection,
                    SqlConnectionInfo.DatabaseOf(connection)));
        }
    }

    private static InterviewOutcome ResolveFile(
        DataSourceKind kind, TextReader reader, TextWriter writer, string? workspaceRoot)
    {
        while (true)
        {
            writer.Write($"{kind.ToString().ToUpperInvariant()} file path (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (text.Length == 0)
                continue;

            // Relative to the workspace, as `build app from csv <file>` is, not to wherever the
            // console happened to be started.
            var path = DataSourcePath.Resolve(Unquote(text), workspaceRoot);

            // A mistyped path is the likeliest thing to go wrong here, so re-ask rather than
            // abort - the same way ResolveTarget re-asks on an unknown target key.
            if (!File.Exists(path))
            {
                writer.WriteLine($"  No file at {path}.");
                continue;
            }

            return new InterviewOutcome(
                InterviewDecision.Source, new DataSourceSpec(kind, path));
        }
    }

    /// <summary>A value pasted from Explorer, or a connection string typed with quotes
    /// around it, arrives quoted.</summary>
    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;

    private static bool IsCancel(string text)
        => string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase);
}
