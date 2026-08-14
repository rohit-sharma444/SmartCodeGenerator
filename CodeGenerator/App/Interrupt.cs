namespace CodeGenerator.App;

/// <summary>
/// Ctrl-C policy: the first press asks, the second insists.
/// </summary>
/// <remarks>
/// Nothing handled Ctrl-C before. During a Wikipedia fetch or a three-minute build it killed the
/// process outright — possibly mid-write, leaving a half-generated project on disk with no
/// indication of what had completed. Letting the current write finish is the whole point.
/// <para>
/// The second press is equally deliberate. A trap that cannot be escaped is worse than no trap: if
/// the first press did not stop whatever is running — and plenty of the work here is synchronous
/// and takes no token — the user must still be able to leave.
/// </para>
/// <para>
/// Static because there is one console and one key press. <see cref="Reset"/> is called when the
/// prompt regains control, so the next command does not begin already cancelled.
/// </para>
/// </remarks>
internal static class Interrupt
{
    private static CancellationTokenSource _source = new();

    /// <summary>Whether cancellation has been asked for and not yet cleared.</summary>
    public static bool Requested { get; private set; }

    /// <summary>
    /// A token cancelled by the first press, for the work that can honour one.
    /// </summary>
    /// <remarks>
    /// Read afresh each time rather than cached by a caller: <see cref="Reset"/> replaces the
    /// source, and a caller holding the old token would wait on something already cancelled.
    /// </remarks>
    public static CancellationToken Token => _source.Token;

    /// <summary>
    /// Records a press and says whether the runtime should be allowed to terminate the process.
    /// </summary>
    /// <returns>
    /// False on the first press — cancellation is requested and the process lives. True on a
    /// second, which is the user insisting.
    /// </returns>
    public static bool ShouldTerminate()
    {
        if (Requested)
            return true;

        Requested = true;

        // Cancel() can throw ObjectDisposedException if a Reset raced with this; a Ctrl-C handler
        // is the last place that should surface an exception.
        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return false;
    }

    /// <summary>Clears the request and issues a fresh token. Called when the prompt takes over.</summary>
    public static void Reset()
    {
        var previous = _source;

        _source = new CancellationTokenSource();
        Requested = false;

        previous.Dispose();
    }
}
