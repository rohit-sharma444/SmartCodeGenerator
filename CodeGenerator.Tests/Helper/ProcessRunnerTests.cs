using System;
using System.ComponentModel;
using System.Diagnostics;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Helper;

[TestClass]
public class ProcessRunnerTests
{
    /// <summary>
    /// A child that writes ~180 KB to stdout and ~180 KB to stderr, interleaved.
    /// Both far exceed the OS pipe buffer, so a parent that fails to drain one
    /// stream while waiting on the other blocks the child forever.
    /// </summary>
    private static (string FileName, string Arguments) FloodBothStreams()
    {
        var line = new string('x', 60);
        return OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c for /L %i in (1,1,3000) do @(echo {line}& echo {line} 1>&2)")
            : ("/bin/sh", $"-c \"i=0; while [ $i -lt 3000 ]; do echo {line}; echo {line} 1>&2; i=$((i+1)); done\"");
    }

    private static (string FileName, string Arguments) SleepFor(int seconds)
        => OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c ping -n {seconds + 1} 127.0.0.1 > nul")
            : ("/bin/sh", $"-c \"sleep {seconds}\"");

    [TestMethod]
    public void Run_ChildFloodsBothPipes_DoesNotDeadlockAndCapturesBoth()
    {
        var (fileName, arguments) = FloodBothStreams();

        var result = ProcessRunner.Run(fileName, arguments, timeout: TimeSpan.FromSeconds(45));

        Assert.IsFalse(result.TimedOut, "The child exits on its own; a timeout here means we deadlocked.");
        Assert.AreEqual(0, result.ExitCode);

        // If either stream were left undrained the child would have blocked long
        // before emitting this much, so the sizes are the real assertion.
        Assert.IsGreaterThan(50_000, result.StandardOutput.Length,
            $"stdout was only {result.StandardOutput.Length} bytes");
        Assert.IsGreaterThan(50_000, result.StandardError.Length,
            $"stderr was only {result.StandardError.Length} bytes");
    }

    [TestMethod]
    public void Run_ChildOutlivesTheTimeout_IsKilledAndReported()
    {
        var (fileName, arguments) = SleepFor(30);

        var stopwatch = Stopwatch.StartNew();
        var result = ProcessRunner.Run(fileName, arguments, timeout: TimeSpan.FromSeconds(2));
        stopwatch.Stop();

        Assert.IsTrue(result.TimedOut);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"Run returned after {stopwatch.Elapsed}, so the timeout did not kill the child.");
    }

    [TestMethod]
    public void Run_ChildExitsNonZero_ReportsExitCodeAndStderr()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c echo boom 1>&2& exit /b 3")
            : ("/bin/sh", "-c \"echo boom 1>&2; exit 3\"");

        var result = ProcessRunner.Run(fileName, arguments);

        Assert.IsFalse(result.TimedOut);
        Assert.AreEqual(3, result.ExitCode);
        StringAssert.Contains(result.StandardError, "boom");
    }

    [TestMethod]
    public void Run_ExecutableDoesNotExist_ThrowsRatherThanHanging()
    {
        Assert.ThrowsExactly<Win32Exception>(
            () => ProcessRunner.Run("no-such-executable-9f3a2b", "--help"));
    }
}
