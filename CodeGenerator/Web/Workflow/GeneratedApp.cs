using AppScaffolding;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// What the generate stage produced, as the run stage needs it.
/// </summary>
/// <remarks>
/// The build log is carried, not just the exit code: <c>AppLauncher</c> reads the built
/// assembly's path out of MSBuild's own output, so a run stage without the log could only start
/// the app the slow way, through <c>dotnet run</c>.
/// </remarks>
/// <param name="Scaffold">What was written, and where.</param>
/// <param name="BuildLog">Everything the compiler said, in order.</param>
/// <param name="BuildSucceeded">Whether that build was green. A red one has nothing to start.</param>
public sealed record GeneratedApp(
    ScaffoldResult Scaffold,
    string BuildLog,
    bool BuildSucceeded);
