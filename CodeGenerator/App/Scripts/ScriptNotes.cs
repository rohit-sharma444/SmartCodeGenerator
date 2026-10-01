using DataIngestion;

namespace CodeGenerator.App.Scripts;

/// <summary>What a saved session holds for commands it cannot replay as typed.</summary>
internal static class ScriptNotes
{
    /// <summary>A build chosen on the build page: its typed line says too little to replay it.
    /// Masked the way the history file masks it — a saved script is shared more than history.</summary>
    public static string InteractiveBuildApp(string input) =>
        "# " + ConnectionSecret.RedactLine(input.Trim())
        + " (built interactively; add its answers below to replay it)";

    public const string Demo = "# demo (a guided walkthrough; type demo to run it again)";
}
