namespace AppScaffolding;

/// <summary>Outcome of scaffolding: the project folder, the files written, and (when a companion
/// test project was emitted) its project path.</summary>
/// <param name="ScreenSummary">
/// How the discovered model was split into screens — "3 masters, 2 transactions (Order has 4
/// lines)". Empty when nothing was discovered. Last, and defaulted, so the existing construction
/// sites are untouched.
/// </param>
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message,
    string? TestProjectPath = null,
    string ScreenSummary = "")
{
    /// <summary>
    /// The write-once validator stubs this run kept that still declare the hand-written hook in
    /// the shape emitted before the validation pipeline, one sentence each.
    /// </summary>
    /// <remarks>
    /// Also in <see cref="Message"/>, and here as well because <see cref="Message"/> is a
    /// failure-only channel on the console: <c>BuildAppCommand</c> prints it under "Generation
    /// failed" and nowhere else, so a note folded only into it would never be read on the run
    /// that matters — a regenerate that SUCCEEDS and leaves a folder that cannot compile. An
    /// init-only property rather than another positional parameter, so every existing
    /// construction site is untouched.
    /// </remarks>
    public IReadOnlyList<string> OutdatedValidatorStubs { get; init; } = [];

    /// <summary>
    /// Why no test project came out of a run that should have made one — one sentence, or null
    /// when the test project was made or was never due (a model with no classes).
    /// </summary>
    /// <remarks>
    /// Its own property for the reason <see cref="OutdatedValidatorStubs"/> is: the scaffold
    /// SUCCEEDS without it, so <see cref="Message"/> would carry it only on the failure path. It
    /// was silent before it existed, and every WPF and WinForms app came out with no test project
    /// and an empty <c>&lt;App&gt;.Tests</c> folder, because `dotnet new xunit` refused
    /// net10.0-windows.
    /// </remarks>
    public string? TestProjectProblem { get; init; }

    /// <summary>
    /// One sentence per field whose imported values its type could not read, so they were left
    /// out of the seed data — usually a field the user retyped in the review loop. Empty when
    /// every value was written.
    /// </summary>
    /// <remarks>
    /// Its own property for the reason <see cref="OutdatedValidatorStubs"/> is: the scaffold
    /// succeeds without these values, so <see cref="Message"/> would never carry it.
    /// </remarks>
    public IReadOnlyList<string> UnreadableSeedValues { get; init; } = [];
}
