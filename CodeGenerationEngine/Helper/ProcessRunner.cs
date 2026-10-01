using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CodeGenerationEngine.Helper;

/// <summary>The outcome of a child process run by <see cref="ProcessRunner"/>.</summary>
/// <param name="ExitCode">The child's exit code, or -1 when it was killed for exceeding the timeout.</param>
/// <param name="StandardOutput">Everything the child wrote to stdout.</param>
/// <param name="StandardError">Everything the child wrote to stderr.</param>
/// <param name="TimedOut">True when the child outlived the timeout and was killed.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

/// <summary>
/// Runs a child process to completion, capturing both output streams under a
/// bounded timeout.
/// </summary>
public static class ProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait for the pipes to drain after killing a child.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/> and waits
    /// up to <paramref name="timeout"/> (default <see cref="DefaultTimeout"/>) for it to
    /// exit, killing its process tree if it does not.
    /// </summary>
    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    public static ProcessResult Run(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Both pipes must be drained concurrently. Reading one to the end while the
        // child fills the other blocks the child on write and us on read: `dotnet add
        // package` emits enough restore output to hit this every time.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        var limit = timeout ?? DefaultTimeout;
        var limitMs = (int)Math.Clamp(limit.TotalMilliseconds, 0, int.MaxValue);

        if (!process.WaitForExit(limitMs))
        {
            KillTree(process);
            return new ProcessResult(-1, Drain(stdout), Drain(stderr), TimedOut: true);
        }

        // The timed overload returns as soon as the child exits; the parameterless one
        // additionally waits for the redirected streams to reach end-of-file.
        process.WaitForExit();

        return new ProcessResult(
            process.ExitCode, Drain(stdout), Drain(stderr), TimedOut: false);
    }

    /// <summary>
    /// Same as <see cref="Run"/>, but hands each output line over as it arrives.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> reads both streams with <c>ReadToEndAsync</c>, so nothing it runs can
    /// report progress — a three-minute build appears all at once at the end. This overload
    /// exists for the callers that want to show it happening, and accumulates the same complete
    /// output into its result so it is a drop-in replacement.
    /// <para>
    /// <see cref="Run"/> is deliberately untouched: its existing callers and their tests depend
    /// on it exactly as it is.
    /// </para>
    /// </remarks>
    /// <param name="onLine">
    /// Called on a background thread, once per line, for both stdout and stderr. A caller
    /// updating a UI must marshal to its own thread.
    /// </param>
    /// <param name="standardInput">Written to the child's input whole, then closed; null leaves
    /// the input unredirected.</param>
    /// <param name="environment">Variables set in the child's environment, on top of this
    /// process's.</param>
    /// <param name="outputEncoding">How to read the child's output; null keeps the console code
    /// page, which is what the tools the existing callers run write.</param>
    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    public static ProcessResult RunStreaming(
        string fileName,
        string arguments,
        Action<string> onLine,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null,
        Encoding? outputEncoding = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? string.Empty,
                RedirectStandardInput = standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (standardInput is not null)
            process.StartInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding;
            process.StartInfo.StandardErrorEncoding = outputEncoding;
        }

        if (environment is not null)
            foreach (var (name, value) in environment)
                process.StartInfo.Environment[name] = value;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // A null Data is the stream closing, not an empty line — forwarding it would hand the
        // caller a blank line for every process that ever exits.
        process.OutputDataReceived += (_, e) => Collect(e.Data, stdout, onLine);
        process.ErrorDataReceived += (_, e) => Collect(e.Data, stderr, onLine);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Written whole and closed, so a child reading to end-of-input sees it end.
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var limit = timeout ?? DefaultTimeout;
        var limitMs = (int)Math.Clamp(limit.TotalMilliseconds, 0, int.MaxValue);

        if (!process.WaitForExit(limitMs))
        {
            KillTree(process);

            return new ProcessResult(-1, Text(stdout), Text(stderr), TimedOut: true);
        }

        // The timed overload returns as soon as the child exits; the parameterless one
        // additionally waits for the async readers to reach end-of-file, without which the last
        // lines can be missing from both the callback and the result.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, Text(stdout), Text(stderr), TimedOut: false);
    }

    /// <summary>Accumulates a line and forwards it. Null means the stream closed.</summary>
    private static void Collect(string? line, StringBuilder sink, Action<string> onLine)
    {
        if (line is null)
            return;

        // Both handlers run on threadpool threads and a caller may read the builder from a
        // third, so every touch of one is serialized on the builder itself.
        lock (sink)
            sink.AppendLine(line);

        onLine(line);
    }

    /// <summary>The accumulated text, read under the same lock the handlers append under.</summary>
    private static string Text(StringBuilder sink)
    {
        lock (sink)
            return sink.ToString();
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit((int)DrainTimeout.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Raced with the child exiting on its own, or the OS refused. Either way
            // there is nothing left to wait for.
        }
    }

    /// <summary>
    /// Awaits a reader that should already be finished. Killing a child closes its
    /// pipes, but a wedged grandchild can hold the write handle open, so this never
    /// waits indefinitely.
    /// </summary>
    private static string Drain(Task<string> reader)
    {
        try
        {
            return reader.Wait(DrainTimeout) ? reader.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
    }
}
