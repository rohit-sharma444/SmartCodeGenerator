using System.ComponentModel;
using System.Diagnostics;

namespace CodeGenerator.App;

/// <summary>
/// Starts the app that was just generated, detached, in a window of its own.
/// </summary>
/// <remarks>
/// Deliberately not <c>ProcessRunner</c>: that redirects both pipes and waits for the child,
/// which is right for <c>dotnet new</c> and wrong here. The user's app may live for hours, and
/// the CLI has to hand the prompt straight back.
/// </remarks>
internal static class AppLauncher
{
    /// <summary>Separates the project name from its output path in MSBuild's console logger.</summary>
    private const string Arrow = " -> ";

    /// <summary>
    /// The executable MSBuild reported building, or null if <paramref name="buildOutput"/> did
    /// not say.
    /// </summary>
    /// <remarks>
    /// The logger writes one <c>Name -&gt; path.dll</c> line per project, dependencies first, so
    /// the last is the app itself. The apphost beside that dll is what gets started rather than
    /// the dll: running it through <c>dotnet</c> is what parks a console window behind a WPF app.
    /// </remarks>
    public static string? AssemblyPathFrom(string buildOutput)
    {
        string? assembly = null;

        foreach (var line in buildOutput.Split('\n'))
        {
            var arrow = line.LastIndexOf(Arrow, StringComparison.Ordinal);

            if (arrow < 0)
                continue;

            var candidate = line[(arrow + Arrow.Length)..].Trim();

            // Rooted-and-.dll is what separates a build line from prose that happens to contain
            // an arrow. Both checks are needed: "an arrow -> like this one" passes neither.
            if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && Path.IsPathRooted(candidate))
                assembly = candidate;
        }

        return assembly is null ? null : Path.ChangeExtension(assembly, ".exe");
    }

    /// <summary>
    /// Starts the generated app and returns what it started, or null if it could not be started.
    /// </summary>
    /// <remarks>
    /// Falls back to <c>dotnet run</c> when the executable cannot be located — a slower start and
    /// an extra console window, but it works from the project path alone, which is the one thing
    /// we always have.
    /// </remarks>
    /// <param name="projectPath">Folder of the generated project.</param>
    /// <param name="buildOutput">MSBuild's console output, read for the built assembly's path.</param>
    /// <param name="fromProjectFolder">
    /// Start through <c>dotnet run</c> even when the executable is known. A web application has to:
    /// its content root is wherever it is started from, and <c>wwwroot</c> is not copied to
    /// <c>bin</c>, so launching the apphost there serves the app with none of its stylesheets
    /// ("The WebRootPath was not found" in its own log). <c>dotnet run</c> roots it at the project
    /// instead, which also applies the port in <c>launchSettings.json</c>.
    /// </param>
    public static string? Launch(
        string projectPath, string buildOutput, bool fromProjectFolder = false)
    {
        if (fromProjectFolder)
            return RunThroughDotNet(projectPath);

        var executable = AssemblyPathFrom(buildOutput);

        return executable is not null && File.Exists(executable)
            ? Start(executable, string.Empty, Path.GetDirectoryName(executable))
              ?? RunThroughDotNet(projectPath)
            : RunThroughDotNet(projectPath);
    }

    private static string? RunThroughDotNet(string projectPath)
        => Start("dotnet", $"run --project \"{projectPath}\"", projectPath);

    /// <summary>
    /// Best-effort start of a detached child. Returns what was started, or null when the OS
    /// refused — a failed launch is a disappointment, never a reason to take down the CLI.
    /// </summary>
    private static string? Start(string fileName, string arguments, string? workingDirectory)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? string.Empty,

                // The child gets its own window and its own stdio. Redirecting instead would
                // interleave a console app's output with this prompt, and waiting on it would
                // freeze the CLI for as long as the user keeps their app open.
                UseShellExecute = true
            });

            return started is null ? null : Path.GetFileName(fileName);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                       or System.IO.FileNotFoundException)
        {
            return null;
        }
    }
}
