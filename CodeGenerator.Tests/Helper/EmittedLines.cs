namespace CodeGenerator.Tests.Helper;

/// <summary>
/// The two shapes every emitted-source test needs. Six copies of the first and four of the
/// second existed before this; they agreed, and nothing held them together.
/// </summary>
internal static class EmittedLines
{
    /// <summary>
    /// <paramref name="source"/> with every line ending reduced to \n.
    /// </summary>
    /// <remarks>
    /// Raw string literals carry their SOURCE FILE's line endings, so a template in a CRLF file
    /// emits CRLF and one in an LF file emits LF — in the same generated app. An assertion about
    /// a multi-line emitted fragment is otherwise a test of which file the template happens to
    /// live in.
    /// </remarks>
    public static string Normalized(string source)
        => source.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>How many times <paramref name="needle"/> occurs in <paramref name="source"/>,
    /// counting non-overlapping ordinal matches.</summary>
    public static int Occurrences(string source, string needle)
    {
        var count = 0;
        var at = source.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
