namespace CodeGenerator.App;

/// <summary>One step of the demo: what it is about to do, and the command that does it.</summary>
internal sealed record DemoStep(string Narration, string Command);

/// <summary>
/// The walkthrough <c>demo</c> runs.
/// </summary>
/// <remarks>
/// <c>demo</c> used to print a canned <c>Customer</c> class to stdout: it created nothing, explained
/// nothing, and ran no workflow, while the help catalog called it one. For someone typing it to see
/// what the tool does, that was a dead end that read as broken.
/// <para>
/// These are real commands, run through the same pipeline as anything typed at the prompt — so the
/// demo cannot drift from the tool's actual behaviour, and a step that stopped working would fail
/// visibly in front of the user rather than quietly.
/// </para>
/// </remarks>
internal static class DemoScript
{
    /// <summary>
    /// The project the demo creates.
    /// </summary>
    /// <remarks>
    /// Named to be obviously disposable and unlikely to collide with anything a user has. The demo
    /// still refuses to run if it already exists, rather than writing into someone else's project.
    /// </remarks>
    public const string ProjectName = "CodeGenDemo";

    public static IReadOnlyList<DemoStep> Steps { get; } =
    [
        new("Create a project to work in",
            $"create console project {ProjectName}"),

        new("Select it, so later commands act on it",
            $"use project {ProjectName}"),

        new("Create a class with two properties",
            "create class Invoice with properties Reference string, Amount decimal"),

        new("Require a value for one of them",
            "add validation Reference is required on Reference in Invoice"),

        // A data rule: one field. It was "add business validation", whose parser reads only rules
        // naming two fields, so this step failed in front of every user.
        new("Add a second rule, on the other property",
            "add validation must be greater than 0 on Amount in Invoice"),

        new("Generate the validator those rules describe",
            "generate validator Invoice"),

        new("Read back what was built",
            "show class Invoice"),
    ];

    /// <summary>What the user is told before anything is written.</summary>
    /// <remarks>
    /// Names the path first. The demo writes to disk, and a walkthrough that creates a project
    /// without saying where would be the same discourtesy as the old overwrite behaviour.
    /// </remarks>
    public static IReadOnlyList<string> Intro(string workspaceRoot) =>
    [
        "This walkthrough runs seven real commands and creates one project:",
        string.Empty,
        $"    {Path.Combine(workspaceRoot, ProjectName)}",
        string.Empty,
        "Nothing else in your workspace is touched, and you can delete that folder afterwards.",
    ];

    /// <summary>What the user is told at the end.</summary>
    public static IReadOnlyList<string> Outro(string projectPath) =>
    [
        string.Empty,
        "That is the class-by-class flow. For a whole application from one sentence, try:",
        string.Empty,
        "    build app: a library that lends books to members",
        string.Empty,
        $"The demo project is at {projectPath} — delete the folder when you are done with it.",
    ];
}
