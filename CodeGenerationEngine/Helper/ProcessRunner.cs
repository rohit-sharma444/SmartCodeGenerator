using System.ComponentModel;
using System.Diagnostics;

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
