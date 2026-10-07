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

    // What the review prints when it is cancelled. A build that failed before its review reads the
    // same answer lines as commands of their own, so "cancel" as the last line proves nothing.
    public const string CancelledMarker = "Cancelled. Nothing was generated.";

    /// <summary>What the copy puts before an answer it refused, or an app it will not replace.
    /// Printed only in a script step — see <see cref="IsStep"/>.</summary>
    public const string RefusedMarker = "Refused: ";

    /// <summary>Set in the copy's environment, so it knows it is running a script's step.</summary>
    public const string StepVariable = "CODEGEN_SCRIPT_STEP";

    /// <summary>Whether this process is a copy running a script's build step.</summary>
    public static bool IsStep => Environment.GetEnvironmentVariable(StepVariable) == "1";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static string DefaultExecutable =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "CodeGenerator.exe" : "CodeGenerator");

    /// <param name="onLine">Called once per output line, never concurrently.</param>
    public static bool Run(
        IReadOnlyList<string> lines, string workspaceRoot, Action<string> onLine, string? executable = null)
    {
        var gate = new object();
        var generated = false;
        var cancelled = false;
        var refused = false;
        var prompts = 0;
        string? ranAfter = null;
        string? generatedAt = null;
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
                        var marker = line.IndexOf(SuccessMarker, StringComparison.Ordinal);
                        if (marker >= 0)
                        {
                            generated = true;
                            generatedAt ??= line[(marker + SuccessMarker.Length)..].Trim();
                        }
                        cancelled |= line.Contains(CancelledMarker, StringComparison.Ordinal);
                        refused |= line.Contains(RefusedMarker, StringComparison.Ordinal);

                        // The copy's own prompt, echoing the line it read. The first is the build
                        // line and the last the closing exit; any other line read there was read
                        // after the build had ended, and ran as a command of its own.
                        if (line.StartsWith(CommandLineEditor.Prompt, StringComparison.Ordinal)
                            && ++prompts > 1
                            && line[CommandLineEditor.Prompt.Length..].Trim() is { Length: > 0 } read
                            && !read.Equals("exit", StringComparison.OrdinalIgnoreCase))
                            ranAfter ??= read;
                        onLine(line);
                    }
                },
                workingDirectory: workspaceRoot,
                timeout: Timeout,
                standardInput: input,
                environment: new Dictionary<string, string> { ["PROJECTPATH"] = workspaceRoot, [StepVariable] = "1" },
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

            // Typed, the build registers the project it generates, and the first one registered
            // with nothing selected becomes the selection - so the next create class goes into it.
            // The copy did that in its own memory; without the same here, a replayed script put
            // the next class in the workspace's Generated folder instead.
            lock (gate)
                if (ProjectManager.CurrentProject is null && generatedAt is not null)
                    ProjectManager.CurrentProject = ProjectManager.GetProjects().FirstOrDefault(p =>
                        string.Equals(Path.GetFullPath(p.ProjectPath), Path.GetFullPath(generatedAt),
                            StringComparison.OrdinalIgnoreCase));
        }

        if (result.TimedOut)
        {
            onLine("The build took longer than 10 minutes and was stopped.");
            return false;
        }

        lock (gate)
        {
            if (ranAfter is not null)
            {
                onLine($"'{ranAfter}' ran after the build had ended, as a command of its own, so the build is not the one the script describes.");
                return false;
            }

            return !refused && (generated || cancelled);
        }
    }
}
