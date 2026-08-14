namespace CodeGenerator.App;

/// <summary>
/// Stops long output for breath instead of scrolling it past the top of the screen.
/// </summary>
/// <remarks>
/// <c>help all</c> is around 250 lines and a large candidate model can be dozens. Both used to
/// disappear upward, which is a poor answer to a question the user just asked.
/// <para>
/// The decisions are separated from the printing and tested; the printing needs a console and is
/// not.
/// </para>
/// </remarks>
internal static class Pager
{
    /// <summary>Rows kept back from a page: the "more" line, and the prompt drawn afterwards.</summary>
    private const int Reserved = 2;

    /// <summary>
    /// Whether output of <paramref name="lineCount"/> lines should be paged.
    /// </summary>
    /// <remarks>
    /// Never when redirected — a pager waiting on a key press in a pipe is a hang, and a paged
    /// transcript is not what the caller asked for. Never when the window height is unknown either:
    /// <see cref="Console.WindowHeight"/> can be 0 or throw with no console attached, and a host
    /// that cannot report its height cannot answer a key press either.
    /// </remarks>
    public static bool ShouldPage(int lineCount, int windowHeight, bool redirected)
        => !redirected && windowHeight > Reserved && lineCount > windowHeight - Reserved;

    /// <summary>Lines per page. Never zero, which would loop forever.</summary>
    public static int PageSize(int windowHeight) => Math.Max(1, windowHeight - Reserved);

    /// <summary>Whether the answer at a "more" prompt means stop.</summary>
    /// <remarks>Null is end-of-input: nothing more is coming, so stop rather than spin.</remarks>
    public static bool ShouldStop(string? answer)
    {
        if (answer is null)
            return true;

        var text = answer.Trim();

        return text.Equals("q", StringComparison.OrdinalIgnoreCase)
               || text.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes <paramref name="lines"/>, pausing a page at a time when that is warranted.
    /// </summary>
    public static void Write(IReadOnlyList<string> lines)
    {
        if (!ShouldPage(lines.Count, WindowHeight(), Console.IsOutputRedirected
                                                    || Console.IsInputRedirected))
        {
            foreach (var line in lines)
                Console.WriteLine(line);

            return;
        }

        var page = PageSize(WindowHeight());

        for (var i = 0; i < lines.Count; i++)
        {
            Console.WriteLine(lines[i]);

            var lastOfPage = (i + 1) % page == 0;

            if (!lastOfPage || i == lines.Count - 1)
                continue;

            Console.Write($"-- {lines.Count - i - 1} more (Enter to continue, q to stop) --");

            var answer = Console.ReadLine();

            // Erase the prompt so the output reads as one listing rather than being punctuated by
            // the pager's own scaffolding.
            Console.Write('\r');
            Console.Write(new string(' ', 60));
            Console.Write('\r');

            if (ShouldStop(answer))
                return;
        }
    }

    /// <summary>
    /// The console height, or 0 when it cannot be known — which
    /// <see cref="ShouldPage"/> reads as "do not page".
    /// </summary>
    private static int WindowHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException
                                      or ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}
