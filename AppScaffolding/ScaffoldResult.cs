namespace AppScaffolding;

/// <summary>Outcome of scaffolding: the project folder and the class files written.</summary>
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message);
