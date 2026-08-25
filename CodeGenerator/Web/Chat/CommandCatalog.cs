using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// What the composer offers as you type.
/// </summary>
/// <remarks>
/// Filtered from <see cref="CommandDictionary.Commands"/> rather than a list of its own: that is
/// the list <c>help</c> renders and <c>docs/commands.md</c> is generated from, so the dropdown
/// cannot fall behind the catalog. A separate list would.
/// <para>
/// The three commands the chat declines are filtered out. Offering something that cannot be run
/// where it is offered is worse than offering nothing.
/// </para>
/// </remarks>
public static class CommandCatalog
{
    private static readonly string[] Declined = ["build app", "batch", "demo"];

    public static IReadOnlyList<CommandInfo> Matches(string typed, int limit = 8)
    {
        var text = (typed ?? string.Empty).Trim();

        if (text.Length == 0)
            return [];

        var runnable = CommandDictionary.Commands.Where(Runnable);

        // Prefix first, then contains: the command that begins with what was typed is the one
        // being reached for, and a "contains" hit is a second-best guess rather than an equal one.
        var prefix = runnable
            .Where(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command.Length)
            .ToList();

        var contains = runnable
            .Where(c => !prefix.Contains(c)
                        && c.Command.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command.Length);

        return [.. prefix.Concat(contains).Take(limit)];
    }

    /// <summary>
    /// The handful of commands the empty page offers to start with.
    /// </summary>
    /// <remarks>
    /// Named here but resolved out of <see cref="CommandDictionary.Commands"/>, so the wording
    /// beside each one is the catalog's own and cannot drift from what <c>help</c> says. A name
    /// that is renamed out of the catalog drops off this list rather than offering a command that
    /// no longer exists.
    /// <para>
    /// All four only read. The first thing a page suggests should not be something that writes
    /// files to a workspace the reader has not looked at yet.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CommandInfo> Starters()
    {
        string[] wanted = ["list projects", "list classes", "status", "help"];

        return
        [
            .. wanted
                .Select(name => CommandDictionary.Commands.FirstOrDefault(
                    c => string.Equals(c.Command, name, StringComparison.OrdinalIgnoreCase)))
                .OfType<CommandInfo>()
                .Where(Runnable)
        ];
    }

    private static bool Runnable(CommandInfo command)
        => !Declined.Any(d => command.Command.StartsWith(d, StringComparison.OrdinalIgnoreCase));
}
