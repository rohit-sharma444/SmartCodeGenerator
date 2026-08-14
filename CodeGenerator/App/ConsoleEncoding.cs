using System.Text;

namespace CodeGenerator.App;

/// <summary>
/// Makes the console able to print the characters the CLI actually emits.
/// </summary>
/// <remarks>
/// The tool's output contains an em dash in 36 places and, in
/// <see cref="SelectionPrompt"/>'s hint, the arrows <c>↑↓</c>. On a console left at a legacy code
/// page those degrade to <c>?</c> or a box — and the arrows are not in Windows-1252 at all, so the
/// one line telling a user how to drive the picker is exactly the line most likely to be mangled.
/// <para>
/// Verified rather than assumed: enumerating the non-ASCII characters in the emitted strings on a
/// cp1252 host failed outright on <c>U+2191</c>, which is the same failure the console has.
/// </para>
/// </remarks>
internal static class ConsoleEncoding
{
    /// <summary>
    /// Switches the console to UTF-8. Best-effort: returns false if the host refused.
    /// </summary>
    /// <remarks>
    /// Never throws. Setting <see cref="Console.OutputEncoding"/> can fail when there is no console
    /// attached or the handle does not support it, and a tool that cannot start because it could
    /// not improve its own punctuation would be a poor trade. Failing leaves the previous encoding,
    /// which is exactly the behaviour that shipped before this existed.
    /// </remarks>
    public static bool UseUtf8()
    {
        try
        {
            // Already UTF-8 (a modern terminal, or a second call): leave it alone. Reassigning is
            // not free — it recreates the console writer.
            if (Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage)
                return true;

            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException
                                      or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
