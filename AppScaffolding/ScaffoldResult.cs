namespace AppScaffolding;

/// <summary>Outcome of scaffolding: the project folder, the files written, and (when a companion
/// test project was emitted) its project path.</summary>
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message,
    string? TestProjectPath = null);
