using System.ComponentModel;
using System.Diagnostics;
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Node 7's work: name it, write it, compile it, and say what happened.
/// </summary>
/// <remarks>
/// Every rule here is the console path's rule, called rather than copied — the naming through
/// <c>BuildAppCommand.AppNameFor</c>, the collision test through
/// <see cref="GeneratedProjectPath.Exists"/>, the report through <c>BuildReport</c> and
/// <c>NextSteps</c>. Two front ends that disagree about what `approve` produces would be worse
/// than one front end.
/// </remarks>
internal static class GenerateStep
{
    /// <summary>How long to wait for the generated app to compile. Matches the console path's
    /// allowance: a first WPF or EF Core build does real work.</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    public static string NameFor(BuildSession session)
        => BuildAppCommand.AppNameFor(session.Description, session.Source);

    /// <summary>
    /// Whether generating under <paramref name="name"/> would overwrite something. The same test
    /// <c>AppScaffolder</c> uses to decide it is reusing a project, so the question is put exactly
    /// when a regeneration would happen and never on a fresh name.
    /// </summary>
    public static bool Collides(BuildSession session, string name, ITargetScaffolder target)
        => GeneratedProjectPath.Exists(session.WorkspaceRoot, name, target);

    public static string FolderFor(BuildSession session, string name, ITargetScaffolder target)
        => GeneratedProjectPath.Folder(session.WorkspaceRoot, name, target);

    public static ScaffoldResult Scaffold(
        BuildSession session, string name, ITargetScaffolder target, PersistenceKind persistence)
        => new AppScaffolder().Scaffold(Request(session, name, target, persistence));

    /// <summary>What this model has that the app already in the folder does not - the console's
    /// overwrite question asks from the same list.</summary>
    public static IReadOnlyList<AppScaffolding.Incremental.ClassDelta> DeltaFor(
        BuildSession session, string name, ITargetScaffolder target)
        => BuildAppCommand.DeltaAgainst(session.Model!, FolderFor(session, name, target));

    /// <summary>
    /// Writes only the classes the existing app does not have, leaving everything else as it is.
    /// </summary>
    /// <remarks>
    /// The console's [a]dd. The page used to offer only overwrite, rename or cancel, so adding a
    /// class to an app someone had already edited meant regenerating over it.
    /// </remarks>
    public static AppScaffolding.Incremental.IncrementalResult AddNew(
        BuildSession session, string name, ITargetScaffolder target, PersistenceKind persistence)
        => AppScaffolding.Incremental.IncrementalScaffolder.Add(
            Request(session, name, target, persistence),
            AppScaffolding.Incremental.ModelDelta.NewNames(DeltaFor(session, name, target)));

    private static ScaffoldRequest Request(
        BuildSession session, string name, ITargetScaffolder target, PersistenceKind persistence)
        => new(
            session.Model!, name, session.WorkspaceRoot, target, persistence,
            session.ImportedData, session.Customization,

            // Read here rather than inside a template: a template reading the clock would turn
            // every golden-output test red on 1 January.
            DateTime.Now.Year);

    /// <summary>
    /// Compiles what was just written, handing each line over as it arrives.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessRunner.RunStreaming"/> documents that it throws
    /// <see cref="Win32Exception"/> when the executable cannot be started, and on this path that
    /// exception came out of a component event handler — which takes the circuit down, freezes the
    /// page with nothing on it, and has the console report a closed tab for a machine that simply
    /// has no `dotnet` on PATH. The console front end has always caught exactly this and carried
    /// on; catching it here is that rule called rather than copied, which is the whole premise of
    /// this class.
    /// </remarks>
    /// <param name="executable">
    /// The compiler to start. A seam, not a setting: the only caller uses the default, and a test
    /// needs a name that cannot be started to reach the case above.
    /// </param>
    public static BuildAttempt Build(
        string projectPath, Action<string> onLine, string executable = "dotnet")
    {
        var clock = Stopwatch.StartNew();

        try
        {
            var result = ProcessRunner.RunStreaming(
                executable, "build", onLine, projectPath, BuildTimeout);

            clock.Stop();

            return new BuildAttempt(result, clock.Elapsed);
        }
        catch (Win32Exception)
        {
            clock.Stop();

            return new BuildAttempt(null, clock.Elapsed);
        }
    }

    /// <summary>
    /// Whether the run stage has anything to start.
    /// </summary>
    /// <remarks>
    /// Three ways to have nothing: nothing was generated, what was generated did not compile, or
    /// it is a class library — <c>dotnet run</c> on one fails with MSB4057, which is why
    /// <see cref="NextSteps"/> offers a project reference instead.
    /// </remarks>
    public static bool CanRun(GeneratedApp? app, ITargetScaffolder target)
        => app is { BuildSucceeded: true, Scaffold.Success: true }
           && target.ProjectType != DotNetProjectType.ClassLibrary;

    /// <summary>What the console prints once the browser is done.</summary>
    public static BuildOutcome OutcomeFor(ScaffoldResult scaffold, ITargetScaffolder target)
    {
        if (!scaffold.Success)
            return new BuildOutcome(
                SessionPhase.Generated, [$"Generation failed: {scaffold.Message}"]);

        var lines = new List<string> { $"Generated project at {scaffold.ProjectPath}" };

        if (scaffold.TestProjectPath is not null)
            lines.Add($"Generated test project at {scaffold.TestProjectPath}");

        // Before the next steps, because it is the reason the next step will fail: a .Custom.cs
        // stub written before the validation pipeline is never overwritten, so it survives a
        // regenerate declaring a hook the regenerated half no longer has.
        foreach (var stub in scaffold.OutdatedValidatorStubs)
            lines.Add(stub + ".");

        // Printed whether or not the build was green: a user whose app does not compile still
        // needs to know where the seed rows are and how to run it once it does.
        lines.AddRange(NextSteps.Lines(
            target, scaffold.ProjectPath, scaffold.TestProjectPath, scaffold.FilesWritten));

        return new BuildOutcome(SessionPhase.Generated, lines);
    }
}
