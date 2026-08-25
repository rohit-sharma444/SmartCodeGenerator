namespace CodeGenerationEngine.Helper;

/// <summary>
/// Captures everything written to the console for the length of one command, as plain text.
/// </summary>
/// <remarks>
/// For a host that renders a command's output itself rather than letting it reach a terminal — the
/// chat page shows the whole block and tints it by verdict, so colour inside the text would be
/// both wrong and visible as escape sequences.
/// <para>
/// Two things happen together and must be undone together: <c>Console.Out</c> is swapped for a
/// buffer, and colour is suppressed at the point of writing (see
/// <see cref="Output.TransportFor"/>). Suppression cannot be done by reading the environment,
/// because <see cref="ColourDelivery.Current"/> is decided once per process.
/// </para>
/// <para>
/// Both are process-global, so this refuses to nest and its caller is expected to serialize.
/// </para>
/// </remarks>
public sealed class OutputCapture : IDisposable
{
    private readonly StringWriter _buffer = new();
    private readonly TextWriter _previous;

    private OutputCapture(TextWriter previous) => _previous = previous;

    /// <summary>Whether output is being captured, and colour therefore suppressed.</summary>
    public static bool Suppressed { get; private set; }

    public static OutputCapture Start()
    {
        if (Suppressed)
        {
            throw new InvalidOperationException(
                "Output is already being captured. Commands must be run one at a time.");
        }

        var capture = new OutputCapture(Console.Out);

        Console.SetOut(capture._buffer);
        Suppressed = true;

        return capture;
    }

    /// <summary>What has been written so far.</summary>
    public string Text => _buffer.ToString();

    public void Dispose()
    {
        Suppressed = false;
        Console.SetOut(_previous);
        _buffer.Dispose();
    }
}
