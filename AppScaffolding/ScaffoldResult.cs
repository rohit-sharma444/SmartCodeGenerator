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
    string ScreenSummary = "");
