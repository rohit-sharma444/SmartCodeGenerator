using System.Runtime.InteropServices;

namespace CodeGenerationEngine.Helper;

/// <summary>How colour reaches the terminal, if at all.</summary>
public enum ColourTransport
{
    /// <summary>Not at all.</summary>
    None,

    /// <summary>ANSI SGR escape sequences in the output stream.</summary>
    Ansi,

    /// <summary>Windows console attributes — <see cref="Console.ForegroundColor"/>.</summary>
    ConsoleAttributes
}

/// <summary>
/// Decides how to deliver colour, and turns on virtual-terminal processing if it can.
/// </summary>
/// <remarks>
/// <see cref="Console.ForegroundColor"/> is not universal. On Windows it calls
/// <c>SetConsoleTextAttribute</c>, and a host that renders only escape sequences — or that sits
/// behind a pipe, like the VS Code extension panel — drops it entirely. ANSI is the path such hosts
/// do understand, and the one every non-Windows terminal expects.
/// <para>
/// Emitting ANSI blindly is worse than emitting nothing: a console without VT processing prints the
/// sequences literally, so the user reads <c>←[32mSaved…</c>. Hence detect-then-emit — ask Windows
/// to enable VT, and only use escape sequences if it agrees.
/// </para>
/// </remarks>
public static class ColourDelivery
{
    private const int StdOutputHandle = -11;

    /// <summary>ENABLE_VIRTUAL_TERMINAL_PROCESSING.</summary>
    private const uint VirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    /// <summary>
    /// The transport to use, decided once per process.
    /// </summary>
    /// <remarks>
    /// Cached because it turns VT on as a side effect, and because the answer cannot change while
    /// the process runs.
    /// </remarks>
    public static ColourTransport Current { get; } = Decide();

    private static ColourTransport Decide()
    {
        if (!Output.Allowed)
            return ColourTransport.None;

        // Everywhere but Windows, escape sequences are simply how it is done.
        if (!OperatingSystem.IsWindows())
            return ColourTransport.Ansi;

        if (TryEnableVirtualTerminal())
            return ColourTransport.Ansi;

        // VT could not be enabled — most often because stdout is a pipe rather than a console, so
        // GetConsoleMode has nothing to answer about. If colour was explicitly forced, that pipe is
        // a host that intends to render the sequences itself, and falling back to console attributes
        // would send it nothing at all. Escape sequences are the only thing that can cross a pipe.
        return Output.ColourForced ? ColourTransport.Ansi : ColourTransport.ConsoleAttributes;
    }

    /// <summary>
    /// Asks Windows to interpret escape sequences on stdout.
    /// </summary>
    /// <remarks>
    /// Already-on counts as success. Failure is not exceptional — an older console, or a handle that
    /// is not a console — and falls back to the attribute API, which is what shipped before.
    /// </remarks>
    private static bool TryEnableVirtualTerminal()
    {
        try
        {
            var handle = GetStdHandle(StdOutputHandle);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            if (!GetConsoleMode(handle, out var mode))
                return false;

            if ((mode & VirtualTerminalProcessing) != 0)
                return true;

            return SetConsoleMode(handle, mode | VirtualTerminalProcessing);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                      or MarshalDirectiveException)
        {
            return false;
        }
    }
}
