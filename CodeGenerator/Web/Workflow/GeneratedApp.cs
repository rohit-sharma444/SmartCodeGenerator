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
/// <param name="BuildRan">
/// Whether the compiler was started at all. False only when `dotnet` could not be started, which
/// is not the same as a build that failed: the project is written and correct as far as anyone
/// knows, and what the user does next is build it themselves. Defaulted, because every other path
/// through this workflow did run a build.
/// </param>
/// <param name="Persistence">What it stores its rows in. Kept so the replay line can approve with
/// it: chosen on the Generate panel, it was otherwise gone with the panel.</param>
public sealed record GeneratedApp(
    ScaffoldResult Scaffold,
    string BuildLog,
    bool BuildSucceeded,
    bool BuildRan = true,
    PersistenceKind Persistence = PersistenceKind.Json);
