namespace CodeGenerator.Web.Workflow;

/// <summary>What the help page says about one stage.</summary>
public sealed record StageHelpEntry(string Summary, IReadOnlyList<string> Steps, string Next);

/// <summary>
/// The text behind each stage's help icon, and the address that icon opens.
/// </summary>
/// <remarks>
/// <see cref="For"/> is a total switch with no default, so a stage added to
/// <see cref="BuildStage"/> will not compile until it has help — the same trick
/// <see cref="StageGraph.Invalidates"/> uses. The address carries the enum name, lower-cased, which
/// is also what a saved snapshot holds.
/// </remarks>
public static class StageHelp
{
    public static string AddressOf(BuildStage stage) => $"/help/{stage.ToString().ToLowerInvariant()}";

    /// <summary>The stage a name in an address stands for, or null. A number is not a stage.</summary>
    public static BuildStage? Parse(string? name)
        => Enum.GetValues<BuildStage>().Cast<BuildStage?>()
            .FirstOrDefault(s => string.Equals(s.ToString(), name, StringComparison.OrdinalIgnoreCase));

    public static StageHelpEntry For(BuildStage stage) => stage switch
    {
        BuildStage.Describe => new(
            "Say in plain words what you are building.",
            [
                "Type one sentence, for example: a library that lends books to members.",
                "Leave the box empty if you will point at a data file on the next stages.",
            ],
            "Application Type: choose which kind of application to build."),

        BuildStage.AppType => new(
            "Choose the kind of application: for example a desktop app, a web app or a class library.",
            [
                "Pick one kind from the list.",
                "Change the choice later if you need to. The classes you found stay.",
            ],
            "Data: choose where the model comes from."),

        BuildStage.Data => new(
            "Choose where the classes come from: a data file, a SQL Server database, or discovery from the web.",
            [
                "To read a file, type its path. The page shows how many rows and columns it holds.",
                "To read a database, give the connection and tick the tables you want.",
                "To use discovery, leave the source empty.",
                "If you change the source after discovery, the page asks first. The old model is lost.",
            ],
            "Look: set the branding, the theme and the optional features."),

        BuildStage.Look => new(
            "Set how the application looks. Every choice here is optional.",
            [
                "Open the options with the checkbox.",
                "Set the branding, the theme, the layout and the optional layers.",
                "Do nothing to keep the defaults.",
            ],
            "Discover: the tool works out the classes."),

        BuildStage.Discover => new(
            "The tool works out the classes from your description or your file.",
            [
                "Wait. This can take a minute or two when the local model is used.",
                "Read the progress lines to see what the tool does.",
                "If the step fails, the message says why. Click a green stage to go back and change the input.",
            ],
            "Review classes: read the classes and correct them."),

        BuildStage.Review => new(
            "Read the model and correct it before the tool writes anything.",
            [
                "Rename or remove a class on its card.",
                "Rename, retype or delete a property on its card.",
                "Add a class or a field, or promote a suggestion to a class.",
                "Nothing is written until you leave this stage.",
            ],
            "Output: see how the classes connect and which ones get a screen."),

        BuildStage.Output => new(
            "The last look before the tool writes the project.",
            [
                "Read the diagram. It shows how the classes connect.",
                "Read the table. It shows if each class gets a master screen, a transaction screen, or a line inside its parent.",
                "Change a choice in the table if it is wrong.",
                "Copy the command line under the table to repeat the same run later.",
            ],
            "Entry Template: choose how entry forms arrange their fields."),

        BuildStage.EntryTemplate => new(
            "Choose how an entry form arranges its fields.",
            [
                "This choice applies only when the target is WPF and the layout is business.",
                "In other cases the page says that the choice does not apply.",
                "Grouped is the default.",
            ],
            "Generate: the tool writes the project and builds it."),

        BuildStage.Generate => new(
            "The tool writes the project and compiles it.",
            [
                "Click Generate. This is always a deliberate click.",
                "Watch the build log on the page.",
                "If the build fails, the page offers to fix the errors.",
            ],
            "Run: start the finished application."),

        BuildStage.Run => new(
            "Start the finished application and see it work.",
            [
                "Click Run. The application starts as a separate process.",
                "Click Stop on the page to stop it. Do this before you build the same project again.",
                "A class library has nothing to start. The page says so.",
                "Click Done to end the workflow.",
            ],
            "This is the last stage."),

        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}
