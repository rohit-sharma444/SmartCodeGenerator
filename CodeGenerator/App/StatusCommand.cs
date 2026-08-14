namespace CodeGenerator.App;

/// <summary>
/// Recognizes the bare <c>status</c> command, which prints what startup no longer does.
/// </summary>
/// <remarks>
/// The same shape as <see cref="ClearCommand"/> and <see cref="HistoryCommand"/>: a CLI-local
/// command, matched on the exact word and handled before the recognition pipeline. Anything
/// wordier than the bare word is left to the pipeline rather than claimed here.
/// </remarks>
internal static class StatusCommand
{
    public static bool IsStatus(string? input)
        => (input ?? string.Empty).Trim()
            .Equals("status", StringComparison.OrdinalIgnoreCase);
}
