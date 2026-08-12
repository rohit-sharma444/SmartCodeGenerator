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
            Description = "Displays all available commands.",
            Example = "help"
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
            Description = "Runs the demo workflow.",
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
            Description = "Describes a system in plain English, then asks which kind of app to "
                        + "build and whether you have data, discovers a domain model, and opens a "
                        + "review loop before any code is written.",
            Example = "build app: a library that lends books to members"
        },
        new()
        {
            Category = "Build App",
            Command = "build app",
            Description = "Starts the same guided flow, asking for the description first, then the "
                        + "application type, then whether you already have data.",
            Example = "build app"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from <format> <location>",
            Description = "Builds the model from existing data with no prompts when piped; "
                        + "interactively you are still asked the application type. Formats: csv, "
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
                        + "window title, where {company} and {app} are substituted.",
            Example = "build app: a shop company \"Codis Ltd\" theme dark accent #0B6E4F"
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
                        + "classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the "
                        + "default; the wpf and winforms targets are JSON-only). Alias: a.",
            Example = "approve webapi sqlite"
        },
        new()
        {
            Category = "Build App",
            Command = "add <name>",
            Description = "Review loop: adds an entity to the candidate model. The name keeps the "
                        + "shape you type, so a compound survives; several words become one class "
                        + "name.",
            Example = "add cash register"
        },
        new()
        {
            Category = "Build App",
            Command = "remove <name>",
            Description = "Review loop: removes an entity from the candidate model.",
            Example = "remove Reservation"
        },
        new()
        {
            Category = "Build App",
            Command = "rename <old> <new>",
            Description = "Review loop: renames an entity in the candidate model.",
            Example = "rename Member Borrower"
        },
        new()
        {
            Category = "Build App",
            Command = "field <entity> <name>[:<type>]",
            Description = "Review loop: adds a field to an entity. Omit the type to have it inferred; "
                        + "explicit types are string, int, decimal, bool or DateTime.",
            Example = "field Book Title:string"
        },
        new()
        {
            Category = "Build App",
            Command = "unfield <entity> <name>",
            Description = "Review loop: removes a field from an entity.",
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