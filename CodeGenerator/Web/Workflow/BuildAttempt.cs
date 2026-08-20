using CodeGenerationEngine.Helper;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// What came of trying to compile the generated project.
/// </summary>
/// <remarks>
/// A tuple could not say "the compiler never started", and that is a different thing from a build
/// that failed: the project on disk is fine, the log is empty rather than damning, and the user's
/// next move is to build it themselves. Conflating the two is how a machine with no `dotnet` on
/// PATH ends up being told its generated code does not compile.
/// </remarks>
/// <param name="Result">The compiler's own result, or null when it could not be started.</param>
/// <param name="Elapsed">How long the attempt took, for the report either way.</param>
internal sealed record BuildAttempt(ProcessResult? Result, TimeSpan Elapsed)
{
    public bool Ran => Result is not null;
}
