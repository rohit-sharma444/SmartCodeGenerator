using System.Text.RegularExpressions;

namespace CodeGenerator.Web.Chat;

/// <summary>One part of a command's output, and how the page should draw it.</summary>
public abstract record OutputBlock;

/// <summary>Preformatted text, exactly as the command printed it.</summary>
public sealed record TextBlock(string Text) : OutputBlock;

public sealed record CodeBlock(string Heading, string Code) : OutputBlock;

public sealed record TableBlock(
    string Heading,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows) : OutputBlock;

public sealed record ListBlock(string Heading, IReadOnlyList<string> Items) : OutputBlock;

/// <summary>
/// Reads a command's captured output and says how to draw it.
/// </summary>
/// <remarks>
/// Three shapes are recognised, each taken from the code that prints it: <c>show class</c>
/// (<c>ClassValidator.ShowClass</c>), <c>list properties</c>
/// (<c>CommandHandlingService.HandleListProperties</c>) and <c>list classes</c>
/// (<c>ProjectManager.ListClasses</c>).
/// <para>
/// Everything else is one <see cref="TextBlock"/>, and so is anything that does not match its
/// shape <b>exactly</b>. That is the rule this class is built around rather than an oversight:
/// these shapes are one refactor of an unrelated handler away from changing, and output that is
/// half-recognised and half-mangled is worse than a monospace block. A changed shape degrades to
/// preformatted text, which is what the console shows anyway.
/// </para>
/// </remarks>
public static class OutputBlocks
{
    /// <summary>`----- Order -----`, the header ShowClass prints before the file.</summary>
    private static readonly Regex ClassHeader = new(
        @"^-{5}\s(?<name>.+?)\s-{5}$", RegexOptions.Compiled);

    /// <summary>`  int Id` — two spaces, a type, one space, a name.</summary>
    private static readonly Regex PropertyLine = new(
        @"^\s{2}(?<type>\S+)\s(?<name>\S+)$", RegexOptions.Compiled);

    public static IReadOnlyList<OutputBlock> Parse(string input, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        return AsCode(lines)
            ?? AsPropertyTable(lines)
            ?? AsClassList(lines)
            ?? [new TextBlock(text.TrimEnd('\r', '\n'))];
    }

    /// <summary>
    /// The whole file, minus the header ShowClass printed above it. Null when the header is not
    /// there — which is every failure path, all of which print a warning and no source.
    /// </summary>
    private static IReadOnlyList<OutputBlock>? AsCode(string[] lines)
    {
        var header = -1;

        for (var i = 0; i < lines.Length && i < 3; i++)
        {
            if (ClassHeader.IsMatch(lines[i].Trim()))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
            return null;

        var name = ClassHeader.Match(lines[header].Trim()).Groups["name"].Value;
        var code = string.Join("\n", lines.Skip(header + 1)).Trim('\n');

        return code.Length == 0 ? null : [new CodeBlock(name, code)];
    }

    private static IReadOnlyList<OutputBlock>? AsPropertyTable(string[] lines)
    {
        if (lines.Length < 2 || !lines[0].StartsWith("Properties in ", StringComparison.Ordinal))
            return null;

        var rows = new List<IReadOnlyList<string>>();

        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var match = PropertyLine.Match(line);

            // One line off the shape and the whole thing is text. A table with a row missing is a
            // table that lies about the class.
            if (!match.Success)
                return null;

            rows.Add([match.Groups["type"].Value, match.Groups["name"].Value]);
        }

        return rows.Count == 0
            ? null
            : [new TableBlock(lines[0].TrimEnd(':'), ["Type", "Name"], rows)];
    }

    private static IReadOnlyList<OutputBlock>? AsClassList(string[] lines)
    {
        var kept = lines.Where(l => l.Trim().Length > 0).ToList();

        if (kept.Count < 3 || !kept[0].StartsWith("Classes in ", StringComparison.Ordinal))
            return null;

        // The rule of dashes is part of the shape. Without it this is some other output that
        // happens to start with the same words.
        if (kept[1].Trim('-').Length != 0 || kept[1].Length < 3)
            return null;

        return [new ListBlock(kept[0].Trim(), [.. kept.Skip(2).Select(l => l.Trim())])];
    }
}
