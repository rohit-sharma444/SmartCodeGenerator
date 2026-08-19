using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static class CommandDictionary
{
    public static readonly Dictionary<string, string> Synonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"] = "class",
            ["model"] = "class",
            ["object"] = "class",

            ["function"] = "method",
            ["operation"] = "method",

            ["field"] = "property",
            ["attribute"] = "property",

            ["endpoint"] = "api",

            ["storage"] = "repository",

            ["manager"] = "service"
        };

    /// <summary>
    /// Normalizes the input text by replacing synonyms with their canonical forms.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static string Normalize(string text)
    {
        foreach (var pair in Synonyms)
        {
            text = Regex.Replace(text,
                $@"\b{Regex.Escape(pair.Key)}\b",
                pair.Value, RegexOptions.IgnoreCase);
        }
        return text;
    }

    public static readonly IReadOnlyList<CommandInfo> Commands =
    [
#region "Projects"
       new()
        {
            Category = "Projects",
            Command = "create project",
            Description = "Creates a new project.",
            Example = "create project EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "current project",
            Description = "Shows the currently selected project.",
            Example = "current project"
        },
        new()
        {
            Category = "Projects",
            Command = "use project",
            Description = "Switches the active project.",
            Example = "use project EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "list projects",
            Description = "Lists all available projects.",
            Example = "list projects"
        },
        new()
        {
            Category = "Projects",
            Command = "show project",
            Description = "Shows the currently selected project (same as 'current project').",
            Example = "show project"
        },
        new()
        {
            Category = "Projects",
            Command = "create console project",
            Description = "Creates a new console project.",
            Example = "create console project EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "create class library",
            Description = "Creates a new class library.",
            Example = "create class library EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "create webapi project",
            Description = "Creates a new Web API project.",
            Example = "create webapi project EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "create blazor project",
            Description = "Creates a new Blazor project.",
            Example = "create blazor project EmployeeManagement"
        },
        new()
        {
            Category = "Projects",
            Command = "create worker service",
            Description = "Creates a new Worker Service project.",
            Example = "create worker service BackgroundJobs"
        },
        new()
        {
            Category = "Projects",
            Command = "create wpf project",
            Description = "Creates a new WPF project.",
            Example = "create wpf project EmployeeManagement"
        },
        #endregion

#region "Class"
        new()
        {
            Category = "Class",
            Command = "create class",
            Description = "Creates a new class.",
            Example = "create employee class"
        },
        new()
        {
            Category = "Class",
            Command = "create class <class name> with properties",
            Description = "Creates a new class with the specified properties.",
            Example = "create employee class emdcode:int, Name:string"
        },
        new()
        {
            Category = "Class",
            Command = "show class",
            Description = "Displays the class source code.",
            Example = "show class Employee"
        },
        new()
        {
            Category = "Class",
            Command = "list classes",
            Description = "Lists all classes in the current project.",
            Example = "list classes"
        },
        new()
        {
            Category = "Class",
            Command = "list classes in <project name>",
            Description = "Lists all classes in a project.",
            Example = "list classes in <EmployeeManagement>"
        },
        new()
        {
            Category = "Class",
            Command = "delete class",
            Description = "Moves a class to DeletedClasses.",
            Example = "delete class Employee"
        },
        new()
        {
            Category = "Class",
            Command = "restore class",
            Description = "Restores a deleted class.",
            Example = "restore class Employee"
        },
        new()
        {
            Category = "Class",
            Command = "rename class",
            Description = "Rename a class.",
            Example = "rename class Employee to <newclass>"
        },
        new()
        {
            Category = "Class",
            Command = "list deleted classes",
            Description = "Lists all deleted classes.",
            Example = "list deleted classes"
        },
        new()
        {
            Category = "Class",
            Command = "<class name> inherits <base class name>",
            Description = "Adds a base class to a class, keeping any existing interfaces. Aliases: extends, derives from.",
            Example = "Order inherits AuditableBase"
        },
#endregion

#region"RecycleBin"
        new()
        {
            Category = "RecycleBin",
            Command = "delete class <class name>",
            Description = "Moves a class to DeletedClasses.",
            Example = "delete class <Employee>"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "restore class <class name>",
            Description = "Restores a deleted class.",
            Example = "restore class <Employee>"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "restore last class",
            Description = "Restores a deleted class.",
            Example = "restore last class"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "show deleted classes",
            Description = "Displays all deleted classes.",
            Example = "show deleted classes"
        },
#endregion

#region "Properties"        
        new()
        {
            Category = "Properties",
            Command = "add property",
            Description = "Adds a property to a class.",
            Example = "add property Name string to Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "remove property",
            Description = "Removes a property from a class.",
            Example = "remove property Name from Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "show properties <class name>",
            Description = "Lists all properties in a class.",
            Example = "show properties of Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "list properties <class name>",
            Description = "Lists all properties in a class.",
            Example = "list properties of Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "rename property <old> to <new> in <class name>",
            Description = "Renames a property on a class. Aliases: field for property; on/of for in.",
            Example = "rename property Name to FullName in Employee"
        },
#endregion

#region"Methods"        
        new()
        {
            Category = "Methods",
            Command = "add method <method name> to <class name>",
            Description = "Adds a method to a class.",
            Example = "add method Save to Employee"
        },
        new()
        {
            Category = "Methods",
            Command = "show methods <class name>",
            Description = "Lists all methods in a class.",
            Example = "show methods of Employee"
        },
        #endregion

#region "Interfaces"        
        new()
        {
            Category = "Interfaces",
            Command = "create interface <interface name>",
            Description = "Creates a new interface.",
            Example = "create interface IRepository"
        },
        #endregion

#region "Validation"        
        new()
        {
            Category = "Validation",
            Command = "add validation <rule> on <property> in <class name>",
            Description = "Adds a field-level data rule to a class. Rules: required (or 'should not be blank'), "
                        + "must be between <min> and <max>, length must be between <min> and <max>, "
                        + "must be greater/less than <n>, must be a valid email, must match <regex>, "
                        + "must be <value>, <value>, ...",
            Example = "add validation must be between 18 and 65 on Age in Employee"
        },
        new()
        {
            Category = "Validation",
            Command = "generate validator",
            Description = "Generates a validator for a class.",
            Example = "generate validator for Employee"
        },
        new()
        {
            Category = "Validation",
            Command = "show validation <class name>",
            Description = "Show data validation rules for a class.",
            Example = "show validation Employee"
        },
        new()
        {
            Category = "Validation",
            Command = "add business validation <rule> in <class name>",
            Description = "Adds an entity-level business rule to a class.",
            Example = "add business validation EndDate must be after StartDate in Booking"
        },
        new()
        {
            Category = "Validation",
            Command = "show business validation <class name>",
            Description = "Show business rules for a class.",
            Example = "show business validation Booking"
        },
        #endregion

#region "Utility"        
        new()
        {
            Category = "Utility",
            Command = "help",
            Description = "Shows a one-screen overview: a few commands to start with, and every category with its command count.",
            Example = "help"
        },
        new()
        {
            Category = "Utility",
            Command = "help <topic>",
            Description = "Shows one category's commands. A topic is any category name 'help' lists, spelled however you like; a command name works too, so 'help approve' explains that command.",
            Example = "help build app"
        },
        new()
        {
            Category = "Utility",
            Command = "help all",
            Description = "Every command in one list — the complete catalog, which is what plain 'help' used to print.",
            Example = "help all"
        },
        new()
        {
            Category = "Utility",
            Command = "status",
            Description = "Reports the workspace, the projects found and which is selected, any loaded rules, the AI state, and the Wikipedia store and its updater service. Startup prints only what is wrong; this prints everything.",
            Example = "status"
        },
        new()
        {
            Category = "Utility",
            Command = "batch",
            Description = "Enter several commands, review what was detected, then run only the approved ones.",
            Example = "batch"
        },
        new()
        {
            Category = "Utility",
            Command = "demo",
            Description = "Runs a short guided walkthrough: creates a small project, adds a class with two validation rules, generates the validator and shows the result. Asks before writing anything, and names the folder it creates so you can delete it afterwards.",
            Example = "demo"
        },
        new()
        {
            Category = "Utility",
            Command = "exit",
            Description = "Exits the application.",
            Example = "exit"
        },
        new()
        {
            Category = "Utility",
            Command = "clear",
            Description = "Clears the screen (history is kept). Alias: cls.",
            Example = "clear"
        },
        new()
        {
            Category = "Utility",
            Command = "cls",
            Description = "Alias for clear (clears the screen; history is kept).",
            Example = "cls"
        },
        new()
        {
            Category = "Utility",
            Command = "history",
            Description = "Lists the last 20 commands (not itself recorded).",
            Example = "history"
        },
#endregion

#region "Analysis"
        new()
        {
            Category = "Analysis",
            Command = "analyze project",
            Description = "Analyzes the current project.",
            Example = "analyze project"
        },
        new()
        {
            Category = "Analysis",
            Command = "analyze class <class name>",
            Description = "Analyzes a specific class.",
            Example = "analyze class Employee"
        },
        new()
        {
            Category = "Analysis",
            Command = "validate project",
            Description = "Compiles every .cs file in the project as one workspace and reports real errors. "
                        + "Aliases: check project, compile project.",
            Example = "validate project"
        },
#endregion

#region "Documentation"
        // All three phrasings reach the same handler and write DesignDocument.md.
        new()
        {
            Category = "Documentation",
            Command = "generate project report",
            Description = "Generates a design document for the current project.",
            Example = "generate project report"
        },
        new()
        {
            Category = "Documentation",
            Command = "generate design document",
            Description = "Generates a design document for the current project.",
            Example = "generate design document"
        },
        new()
        {
            Category = "Documentation",
            Command = "generate documentation",
            Description = "Generates a design document for the current project.",
            Example = "generate documentation"
        },
#endregion

#region "Learning"
        new()
        {
            Category = "Learning",
            Command = "show learning",
            Description = "Lists learned command signatures and their confidence. Aliases: list learning, view learning.",
            Example = "show learning"
        },
        new()
        {
            Category = "Learning",
            Command = "reset learning",
            Description = "Clears the learned command memory. Aliases: clear learning, forget learning.",
            Example = "reset learning"
        },
#endregion

#region "AI"
        // Handled in the CLI beside history and clear, so these have no IntentType and
        // no LiteralCommandTable entry. They are listed here because help is the command
        // catalog users read, not a mirror of the parser.
        new()
        {
            Category = "AI",
            Command = "ai status",
            Description = "Reports whether AI command recognition is on, the model and index in "
                        + "use, both confidence gates, and how many decisions have been logged. "
                        + "Plain 'ai' does the same.",
            Example = "ai status"
        },
        new()
        {
            Category = "AI",
            Command = "ai enable",
            Description = "Turns AI recognition on in ai.json. Off by default. Restart the CLI "
                        + "afterwards to load the model, then run 'ai reindex'.",
            Example = "ai enable"
        },
        new()
        {
            Category = "AI",
            Command = "ai disable",
            Description = "Turns AI recognition off. Command recognition returns to the "
                        + "rule-based path exactly as it behaves with AI absent.",
            Example = "ai disable"
        },
        new()
        {
            Category = "AI",
            Command = "ai reindex",
            Description = "Rebuilds the phrase index from the current commands. Run it after "
                        + "enabling AI, or whenever 'ai status' reports REINDEX REQUIRED.",
            Example = "ai reindex"
        },
        new()
        {
            Category = "AI",
            Command = "ai mode <retrieval|generative|hybrid>",
            Description = "Sets how AI reads unrecognized input. Only retrieval works today; "
                        + "generative and hybrid are declined until a later phase.",
            Example = "ai mode retrieval"
        },
        new()
        {
            Category = "AI",
            Command = "ai why",
            Description = "Explains the last AI reading: what you typed, how it was read, the "
                        + "supporting examples, and whether you accepted it.",
            Example = "ai why"
        },
#endregion

#region "Build App"
        new()
        {
            Category = "Build App",
            Command = "build app: <description>",
            Description = "Describes a system in plain English. Interactively this opens a "
                        + "workflow page in the browser — the console prints the address and "
                        + "waits — where the rest of the build happens: application type, data, "
                        + "look, domain discovery, model review, generate-and-build, then running "
                        + "the finished application. Every step that takes a while says what it is "
                        + "doing and how long it has been doing it, and the compiler's output "
                        + "appears line by line as it builds. "
                        + "Piped runs are unchanged, and CG_NO_WEB=1 keeps the console wizard.",
            Example = "build app: a library that lends books to members"
        },
        new()
        {
            Category = "Build App",
            Command = "build app",
            Description = "Starts the same flow with nothing filled in. Interactively it opens a "
                        + "workflow page in the browser drawn as eight connected stages, ending "
                        + "in one that starts the application it just built: the one "
                        + "being answered is open, a long step reports its progress as it works, "
                        + "a finished stage can be clicked to go back, and "
                        + "changing the description or the data source discards the model that was "
                        + "discovered from the old one. Piped runs keep the console wizard, as "
                        + "does CG_NO_WEB=1.",
            Example = "build app"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from <format> <location>",
            Description = "Builds the model from existing data with no prompts when piped; "
                        + "interactively the file is already answered and the workflow page opens "
                        + "on what is left. Formats: csv, "
                        + "txt, json, excel (sql is not in this build yet). Add 'normalize' to "
                        + "apply every normalization change the data needs.",
            Example = "build app: order tracking from csv D:\\data\\orders.csv normalize"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [customization options]",
            Description = "Customizes the generated application. Options go LAST on the line, "
                        + "after any data source: company \"<name>\" sets the company (used in "
                        + "the project metadata, the window title and a copyright header); "
                        + "theme light|dark and accent #rrggbb colour the desktop front ends; "
                        + "icon <path> supplies a .ico or .png; title \"<pattern>\" overrides the "
                        + "window title, where {company} and {app} are substituted; "
                        + "namespace <prefix> puts the generated code under that namespace root "
                        + "(CodisLtd.Shop), leaving the project and assembly names alone; "
                        + "layout tabs|sidebar|master-detail|dashboard chooses the shape of the "
                        + "window (wpf and winforms only).",
            Example = "build app: a shop company \"Codis Ltd\" namespace CodisLtd theme dark layout sidebar"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [novalidation] [logging] [noseed]",
            Description = "Chooses which optional layers are generated. 'novalidation' omits the "
                        + "Validation folder and its per-model validators; 'logging' adds a "
                        + "logging layer (off by default); 'noseed' omits the sample seed rows "
                        + "(rows imported from your own data file are unaffected). Interactively "
                        + "these are asked by the 'Customize appearance and features?' step, "
                        + "which remembers your answers in .codegen/app-profile.json.",
            Example = "build app: a shop novalidation logging noseed"
        },
        // The rest of this category is only valid at the review-loop prompt that
        // `build app:` opens, not at the main CodeGenerator prompt.
        new()
        {
            Category = "Build App",
            Command = "approve [target] [sqlite]",
            Description = "Review loop: generates the app using the type chosen at the start. "
                        + "Name a target to override it: wpf (default), winforms, console, webapi, "
                        + "blazor, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the "
                        + "default; the wpf and winforms targets are JSON-only). Alias: a.",
            Example = "approve webapi sqlite"
        },
        new()
        {
            Category = "Build App",
            Command = "add <name>",
            Description = "Review loop: adds a class to the candidate model. The name keeps the "
                        + "shape you type, so a compound survives; several words become one class "
                        + "name.",
            Example = "add cash register"
        },
        new()
        {
            Category = "Build App",
            Command = "remove <name>",
            Description = "Review loop: removes a class from the candidate model.",
            Example = "remove Reservation"
        },
        new()
        {
            Category = "Build App",
            Command = "rename <old> <new>",
            Description = "Review loop: renames a class in the candidate model.",
            Example = "rename Member Borrower"
        },
        new()
        {
            Category = "Build App",
            Command = "field <class> <name>[:<type>]",
            Description = "Review loop: adds a field to a class. Omit the type to have it inferred; "
                        + "explicit types are string, int, decimal, bool or DateTime.",
            Example = "field Book Title:string"
        },
        new()
        {
            Category = "Build App",
            Command = "unfield <class> <name>",
            Description = "Review loop: removes a field from a class.",
            Example = "unfield Book Title"
        },
        new()
        {
            Category = "Build App",
            Command = "why [name]",
            Description = "Review loop: explains what discovery decided. Bare, it reports every "
                        + "decision grouped by outcome; with a name, it shows that name's whole "
                        + "trail — the stage, the reason and the evidence behind each step.",
            Example = "why Cash register"
        },
        new()
        {
            Category = "Build App",
            Command = "cancel",
            Description = "Review loop: abandons the model without generating anything. Alias: c.",
            Example = "cancel"
        },
#endregion
    ];

    /// <summary>Every category in the catalog, once each, in a stable order.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        Commands.Select(c => c.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

    /// <summary>One row of the overview's opening list: a catalog command and a one-line gloss.</summary>
    /// <param name="Command">Must match a <see cref="CommandInfo.Command"/> exactly.</param>
    /// <param name="Summary">Short enough that the row does not wrap on an 80-column console.</param>
    public sealed record StarterCommand(string Command, string Summary);

    /// <summary>
    /// The handful of commands the overview leads with.
    /// </summary>
    /// <remarks>
    /// Hand-picked rather than derived: ranking the catalog by anything measurable just produces
    /// alphabetical noise, and what a new user needs first is a judgement. Each entry names a real
    /// catalog row and a test asserts as much — a curated list is exactly the sort of thing that
    /// rots into advertising a renamed command.
    /// <para>
    /// The summaries are written here rather than taken from <see cref="CommandInfo.Description"/>
    /// because those are reference prose — the one for <c>build app:</c> is over 200 characters,
    /// which wraps into four lines and undoes the point of a one-screen overview.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<StarterCommand> Starters { get; } =
    [
        new("build app: <description>", "describe a system, get an app"),
        new("build app", "guided, or from data you have"),
        new("create class <class name> with properties", "add a class"),
        new("use project", "switch the active project"),
        new("validate project", "compile it, report real errors"),
    ];

    /// <summary>How many categories are listed per line of the overview.</summary>
    private const int CategoriesPerRow = 4;

    /// <summary>
    /// The one-screen answer to <c>help</c>: somewhere to start, then the categories.
    /// </summary>
    /// <remarks>
    /// <see cref="GetHelpText"/> is around 250 lines, which scrolls the prompt — and the command
    /// the banner just told the user to type — off the top of the screen. That is a poor first
    /// answer to "what can this do?", so the full catalog moved behind <c>help all</c>.
    /// </remarks>
    public static string GetOverview()
    {
        var lines = new List<string>
        {
            "Smart Code Generator — commands",
            string.Empty,
            "  Start here",
        };

        // Derived, not a magic 42: a longer starter command would otherwise push its summary
        // out of alignment with the rest.
        var commandWidth = Starters.Max(s => s.Command.Length) + 2;

        foreach (var starter in Starters)
            lines.Add("    " + starter.Command.PadRight(commandWidth) + starter.Summary);

        lines.Add(string.Empty);
        lines.Add("  Categories — `help <name>` for one");

        var labels = Categories
            .Select(c => $"{c} ({Commands.Count(x => x.Category == c)})")
            .ToList();

        var width = labels.Max(l => l.Length) + 3;

        for (var i = 0; i < labels.Count; i += CategoriesPerRow)
        {
            var row = labels.Skip(i).Take(CategoriesPerRow)
                .Select(l => l.PadRight(width));

            lines.Add("    " + string.Concat(row).TrimEnd());
        }

        lines.Add(string.Empty);
        lines.Add($"  help all    every command in one list ({Commands.Count})");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The commands under <paramref name="topic"/> — a category name, or a command name — or null
    /// if it names neither.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string or the whole catalog, so the caller can say what was not
    /// understood and list the alternatives. Answering an unrecognized topic with everything is
    /// the failure this whole change is about.
    /// <para>
    /// A command name is accepted as well as a category because <c>help approve</c> is what
    /// someone stuck on one command actually types, and refusing it on the grounds that it is not
    /// a category would be pedantry.
    /// </para>
    /// </remarks>
    public static string? GetTopicHelp(string? topic)
    {
        var wanted = TopicKey(topic);

        if (wanted.Length == 0)
            return null;

        var category = Categories.FirstOrDefault(c => TopicKey(c) == wanted);

        if (category is not null)
            return Render(category, Commands.Where(c => c.Category == category));

        // Prefix, not equality: catalog entries carry their arguments ("approve [target]
        // [sqlite]"), and nobody types those to ask about one.
        var named = Commands
            .Where(c => TopicKey(c.Command).StartsWith(wanted, StringComparison.Ordinal))
            .ToList();

        return named.Count == 0 ? null : Render(topic!.Trim(), named);
    }

    /// <summary>Case- and space-insensitive, so "Build App", "build app" and "buildapp" are one
    /// topic.</summary>
    private static string TopicKey(string? text)
        => new((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c))
            .Select(char.ToLowerInvariant).ToArray());

    private static string Render(string heading, IEnumerable<CommandInfo> commands)
    {
        var lines = new List<string>
        {
            heading.ToUpperInvariant(),
            new string('-', 80),
        };

        foreach (var command in commands.OrderBy(c => c.Command, StringComparer.Ordinal))
        {
            lines.Add($"{command.Command,-45} {command.Description}");
            lines.Add($"    e.g. {command.Example}");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Every command, grouped by category. What <c>help all</c> prints.
    /// </summary>
    /// <remarks>
    /// Bare <c>help</c> shows <see cref="GetOverview"/> instead. This stayed exactly as it was so
    /// that the guarantee worth keeping — that every catalog entry is reachable from one screen of
    /// output — still has something to be asserted against.
    /// </remarks>
    public static string GetHelpText()
    {
        var lines = new List<string>
        {
            "CodeGenerationEngine Help",
            new string('-', 80),
            string.Empty
        };

        foreach (var group in Commands
                     .OrderBy(c => c.Category)
                     .GroupBy(c => c.Category))
        {
            lines.Add(group.Key.ToUpperInvariant());
            lines.Add(new string('-', 80));

            foreach (var command in group.OrderBy(c => c.Command))
            {
                lines.Add($"{command.Command,-45} {command.Description}");
                lines.Add($"    e.g. {command.Example}");
                lines.Add(string.Empty);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Renders the command catalog as a Markdown document, grouped by category.
    /// This is the single source of truth for <c>docs/commands.md</c>; a test asserts
    /// the committed file matches (regenerate with <c>UPDATE_COMMAND_DOCS=1</c>).
    /// Uses LF line endings so the output is stable across platforms.
    /// </summary>
    public static string GetMarkdownReference()
    {
        var lines = new List<string>
        {
            "# Command Reference",
            string.Empty,
            "<!-- Generated from CommandDictionary.GetMarkdownReference(). Do not edit by hand.",
            "     Regenerate: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`. -->",
            string.Empty
        };

        foreach (var group in Commands
                     .OrderBy(c => c.Category)
                     .GroupBy(c => c.Category))
        {
            lines.Add($"## {group.Key}");
            lines.Add(string.Empty);
            lines.Add("| Command | Description | Example |");
            lines.Add("| --- | --- | --- |");

            foreach (var command in group.OrderBy(c => c.Command))
                lines.Add(
                    $"| `{Cell(command.Command)}` | {Cell(command.Description)} "
                    + $"| `{Cell(command.Example)}` |");

            lines.Add(string.Empty);
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Escapes a value for a Markdown table cell.
    ///
    /// A bare <c>|</c> ends the cell, so a command documenting alternatives —
    /// <c>ai mode &lt;retrieval|generative|hybrid&gt;</c> — silently renders as five
    /// columns and breaks the whole row. Backticks do not protect it; the escape is
    /// still required inside a code span.</summary>
    private static string Cell(string value) => value.Replace("|", @"\|");
}