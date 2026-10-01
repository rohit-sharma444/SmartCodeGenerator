using System.ComponentModel;
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// Runs a script's <c>build app</c> block in a separate copy of the tool, its lines piped in.
/// </summary>
/// <remarks>
/// The piped form is the one <c>build app</c> already has and its end-to-end runs use: no wizard,
/// no page, answers read line by line. Running it in-process would mean changing how the
/// command reads input in a dozen places. The copy's exit code is 0 whatever happens, so success is
/// read from the line it prints when it generates a project.
/// </remarks>
internal static class ScriptBuildApp
{
    public const string SuccessMarker = "Generated project at ";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static string DefaultExecutable =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "CodeGenerator.exe" : "CodeGenerator");

    /// <param name="onLine">Called once per output line, never concurrently.</param>
    public static bool Run(
        IReadOnlyList<string> lines, string workspaceRoot, Action<string> onLine, string? executable = null)
    {
        var gate = new object();
        var generated = false;
        var input = string.Join("\n", lines.Append("exit")) + "\n";

        ProcessResult result;
        try
        {
            result = ProcessRunner.RunStreaming(
                executable ?? DefaultExecutable,
                string.Empty,
                line =>
                {
                    // stdout and stderr arrive on two threads; the chat page's capture is a plain
                    // StringWriter.
                    lock (gate)
                    {
                        generated |= line.Contains(SuccessMarker, StringComparison.Ordinal);
                        onLine(line);
                    }
                },
                workingDirectory: workspaceRoot,
                timeout: Timeout,
                standardInput: input,
                environment: new Dictionary<string, string> { ["PROJECTPATH"] = workspaceRoot },
                outputEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Win32Exception ex)
        {
            onLine($"Could not start a copy of the tool: {ex.Message}");
            return false;
        }
        finally
        {
            // The copy may have registered a project in the file this process also reads.
            ProjectManager.ReloadWithin(workspaceRoot);
        }

        if (result.TimedOut)
        {
            onLine("The build took longer than 10 minutes and was stopped.");
            return false;
        }

        lock (gate)
            return generated || lines[^1].Equals("cancel", StringComparison.OrdinalIgnoreCase);
    }
}
