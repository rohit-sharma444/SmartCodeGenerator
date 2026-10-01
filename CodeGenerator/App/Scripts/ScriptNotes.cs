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

    /// <summary>What a saved session holds for a line that worked: the line, unless only the AI could
    /// read it — a script never asks the AI, so replaying it would fail and put back the whole run.</summary>
    public static string SavedForm(string input, bool neededAi) =>
        neededAi
            ? "# " + input.Trim()
              + " (understood with the AI's help, which a script does not use; write it as a command to replay it)"
            : input;

    /// <summary>A relationship set through the offer after a command. There is no typed form to
    /// replay it with, so the note names what it added.</summary>
    public static string Relationship(string owner, string target, string kind, IEnumerable<string> added) =>
        $"# relationship from the offer, not replayed: {owner} -> {target}, {kind}; added {string.Join(", ", added)}";
}
