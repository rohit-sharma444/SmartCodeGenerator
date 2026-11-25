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
            Description = "Creates a new project: a console project on net10.0 unless you say otherwise. "
                        + "It needs a name - a bare 'create project' is refused. Add a type (console, "
                        + "classlib, webapi, blazor, blazorwasm, wpf, winforms, worker, mvc), a "
                        + "framework such as net8.0, and in '<folder>' to put it somewhere other than "
                        + "a folder of its own name in the workspace.",
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
            Description = "Creates a new class. Refuses a name that already exists, and leaves that file as it is.",
            Example = "create class Employee"
        },
        new()
        {
            Category = "Class",
            Command = "create class <class name> with properties",
            Description = "Creates a new class with the specified properties. After them you can add "
                        + "'and methods <list>', 'inherits <base>' and 'implements <interfaces>'. "
                        + "Refuses a name that already exists. A property can have 'default <value>' "
                        + "or '= <value>', and the modifiers readonly, private, static and required "
                        + "(the C# keyword) after its type, as in 'Id int readonly, Tax decimal = 0'.",
            Example = "create class Employee with properties EmpCode int, Name string"
        },
        new()
        {
            Category = "Class",
            Command = "create record|struct|abstract class|static class|sealed class <name>",
            Description = "Creates a record, a struct, or an abstract, static or sealed class. Every "
                        + "create class form works after the kind word ('with properties', 'and methods', "
                        + "'inherits', 'implements'). A record's properties are init-only; a static class's "
                        + "members are static; an abstract class's listed methods are abstract, and a class "
                        + "that inherits it gets an override of each abstract member. Also 'sealed record' "
                        + "and 'abstract record'. Refuses what C# forbids, such as a static struct.",
            Example = "create record Money with properties Amount decimal and Code string"
        },
        new()
        {
            Category = "Class",
            Command = "create enum <name> with values <value>, ...",
            Description = "Creates a new enum with the values you list, split on commas and 'and'. "
                        + "Refuses a repeated value, a keyword, and a name that already exists.",
            Example = "create enum Status with values Active, Closed, Pending"
        },
        new()
        {
            Category = "Class",
            Command = "add value <value> to <enum name>",
            Description = "Adds a value to an enum, after the last. An enum that numbers its values gives it "
                        + "the next number. Refuses a repeated value, a keyword, and a [Flags] enum.",
            Example = "add value Archived to Status"
        },
        new()
        {
            Category = "Class",
            Command = "remove value <value> from <enum name>",
            Description = "Removes a value from an enum, then names the code that still uses it.",
            Example = "remove value Pending from Status"
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
            Description = "Lists all classes in the current project, or in the workspace when none is selected.",
            Example = "list classes"
        },
        new()
        {
            Category = "Class",
            Command = "list classes in <project name>",
            Description = "Lists all classes in a project.",
            Example = "list classes in EmployeeManagement"
        },
        new()
        {
            Category = "Class",
            Command = "delete class",
            Description = "Moves a class to DeletedClasses, every partial file of it. Drops the "
                        + "validation rules saved for it.",
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
            Description = "Renames a class and every reference to it in the workspace (read by the "
                        + "compiler, so a same-named word in a comment or string is left alone). "
                        + "Refuses a name that already exists.",
            Example = "rename class Employee to Staff"
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
            Description = "Adds a base type, keeping any existing interfaces; an abstract base's members get override stubs. "
                        + "Aliases: extends, derives from.",
            Example = "Order inherits AuditableBase"
        },
        new()
        {
            Category = "Class",
            Command = "<type name> implements <interface>[, <interface> ...]",
            Description = "Adds interfaces to an existing type (a struct or record too) and a stub for each member it lacks; "
                        + "a stub method throws NotImplementedException until you fill it in. Name interfaces only.",
            Example = "Order implements IAuditable, IExportable"
        },
#endregion

#region"RecycleBin"
        new()
        {
            Category = "RecycleBin",
            Command = "delete class <class name>",
            Description = "Moves a class to DeletedClasses.",
            Example = "delete class Employee"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "restore class <class name>",
            Description = "Restores a deleted class by name, to the folder it was deleted from.",
            Example = "restore class Employee"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "restore last class",
            Description = "Restores the class deleted most recently, without naming it.",
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
            Description = "Adds a property to a class or an interface. On an interface it names the "
                        + "classes that implement it and now lack the property. Add 'default <value>' "
                        + "or '= <value>' for a default, and readonly, private, static or required "
                        + "(the C# keyword) before the word property: 'add readonly property Id int to Order'.",
            Example = "add property Name string to Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "remove property",
            Description = "Removes a property from a class or an interface, with the validation rules "
                        + "saved for it, and writes the class's validator again. On an interface it "
                        + "names the implementers it left as they were.",
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
            Description = "Renames a property on a class or an interface. Aliases: field for property; "
                        + "on/of for in.",
            Example = "rename property Name to FullName in Employee"
        },
        new()
        {
            Category = "Properties",
            Command = "change type of <property> to <type> in <class name>",
            Description = "Changes a property's type to any C# type (int, decimal?, List<string>). "
                        + "An initial value that still compiles for the new type is kept; one that does "
                        + "not, and an attribute that only suits text, is removed. The command "
                        + "refuses a change it cannot make safely. Works on interfaces too. "
                        + "Also: retype property <property> to <type> in <class name>.",
            Example = "change type of Pages to int in Book"
        },
#endregion

#region"Methods"        
        new()
        {
            Category = "Methods",
            Command = "add method <method name> to <class name>",
            Description = "Adds a method to a class or an interface. Give a return type with "
                        + "'add method <name> returns <type> to <class name>'. Give parameters as "
                        + "'add method <name>(<type> <name>, ...) to <class name>' or "
                        + "'add method <name> with parameters <name> <type>, ... to <class name>'. "
                        + "On an interface it names the implementers that lack the method. "
                        + "Use 'add abstract method <name> to <class name>' to add a method with no body to an abstract class.",
            Example = "add method ApplyDiscount(decimal percent) returns decimal to Order"
        },
        new()
        {
            Category = "Methods",
            Command = "add constructor [with <property>, ...] to <class name>",
            Description = "Adds a constructor to a class, record or struct. With no list it takes every "
                        + "settable instance property the class declares that is not private, in order; "
                        + "'with Total and Name' takes only those, in that order. Each parameter is "
                        + "assigned to its property. Refused on a static class, an interface, an enum "
                        + "and a positional record, and when the result would not compile.",
            Example = "add constructor with Total and Name to Order"
        },
        new()
        {
            Category = "Methods",
            Command = "show methods <class name>",
            Description = "Lists all methods in a class.",
            Example = "show methods of Employee"
        },
        new()
        {
            Category = "Methods",
            Command = "remove method <method name> from <class name>",
            Description = "Removes a method from a class or an interface. When the name is overloaded it lists "
                        + "them and removes none.",
            Example = "remove method Save from Employee"
        },
        new()
        {
            Category = "Methods",
            Command = "rename method <old> to <new> in <class name>",
            Description = "Renames a method on a class or an interface, every overload of it. References "
                        + "elsewhere are not updated; the command names the code that still uses the old name.",
            Example = "rename method Save to Store in Employee"
        },
        #endregion

#region "Interfaces"        
        new()
        {
            Category = "Interfaces",
            Command = "create interface <interface name>",
            Description = "Creates a new interface. A name that is I followed by a capital letter (IShape) is kept as typed; "
                + "any other name is PascalCased and given the I prefix, so Item makes IItem, invoice makes IInvoice, "
                + "and an all lower case name is always prefixed (ishape makes IIshape - type IShape for that name). "
                + "The reply names the interface when it differs from what you typed. Refuses a name "
                + "that already exists. Add, remove, rename and retype members with the commands "
                + "for properties and methods; they name the classes that implement it. "
                + "A validation rule or a validator cannot go on an interface.",
            Example = "create interface IRepository"
        },
        #endregion

#region "Validation"        
        new()
        {
            Category = "Validation",
            Command = "add validation <rule> on <property> in <class name>",
            Description = "Adds a field-level data rule to a class in plain English. Understands "
                        + "required (or 'should not be blank'), numeric bounds (between, at least, "
                        + "at most, greater/less than, positive, not negative, not zero, whole "
                        + "number), text bounds (length between, at most <n> characters, starts "
                        + "or ends with, contains, uppercase, digits only), named formats (email, "
                        + "URL, phone number, postcode, GUID, IP address, date), relative dates "
                        + "(in the past, in the future, at least <n> years ago, within the last "
                        + "<n> days), must match <regex>, and a list of allowed values. Rules "
                        + "are saved in the workspace (.codegen/validation-rules.json) and are "
                        + "there again in the next session. Refused on an interface.",
            Example = "add validation must be between 18 and 65 on Age in Employee"
        },
        new()
        {
            Category = "Validation",
            Command = "generate validator",
            Description = "Generates a validator for a class. Refused on an interface.",
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
            Description = "Adds an entity-level business rule, spanning more than one property: "
                        + "comparisons (after, before, must not exceed), required-when, "
                        + "either/or, arithmetic (equals A times B, plus, minus, divided by) and "
                        + "sameness (same as, different from). Refused on an interface.",
            Example = "add business validation EndDate must be after StartDate in Booking"
        },
        new()
        {
            Category = "Validation",
            Command = "show business validation <class name>",
            Description = "Show business rules for a class.",
            Example = "show business validation Booking"
        },
        new()
        {
            Category = "Validation",
            Command = "remove validation from <class name>.<property>",
            Description = "Removes every data rule on a field. Also: remove validation on <property> "
                        + "in <class name>; or remove validation <number> from <class name>, "
                        + "with the number show validation prints.",
            Example = "remove validation from Employee.Name"
        },
        new()
        {
            Category = "Validation",
            Command = "remove business validation <number> from <class name>",
            Description = "Removes one business rule, by the number show business validation prints. "
                        + "Also: remove business validation from <class name>.<property> removes "
                        + "every business rule that names that field.",
            Example = "remove business validation 1 from Booking"
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
            Description = "Reports the version, whether colour is on, the workspace, the projects found and which is selected, any loaded rules, the AI state, and the Wikipedia store and its updater service. Startup prints only what is wrong; this prints everything.",
            Example = "status"
        },
        new()
        {
            Category = "Utility",
            Command = "undo",
            Description = "Reverses the last command that changed something: its files, the validation rules and the current project. "
                        + "A whole batch or build app is one step. This session only, up to 50 steps; refuses, naming the file, "
                        + "if you have edited one it would restore. Off for a workspace of more than "
                        + "5,000 files or 200 MB. Loading another folder starts a new history. The "
                        + "chat page has Undo and Redo buttons, with a history of its own.",
            Example = "undo"
        },
        new()
        {
            Category = "Utility",
            Command = "redo",
            Description = "Puts back what undo reversed. The next command that changes something clears it.",
            Example = "redo"
        },
        new()
        {
            Category = "Class",
            Command = "show relationships",
            Description =
                "Draws the classes in the workspace and what each one is related to, as boxes and "
                + "arrows on a page in the browser. A relationship is a pair - an int <Target>Id "
                + "and a property typed as that class - so a class carrying only one half of it is "
                + "drawn apart, which is what the tool offers to finish. Classes related to "
                + "nothing are shown too rather than hidden. Aliases: show relationship, list "
                + "relationships, relationships. A piped run gets the same answer as lines.",
            Example = "show relationships"
        },
        new()
        {
            Category = "Utility",
            Command = "chat",
            Description =
                "Opens a page in the browser where every command except 'build app', 'batch', "
                + "'demo' and 'chat' can be run in a conversation: a command's whole output appears below what "
                + "you typed, tinted green when it worked, red when it failed and amber when the "
                + "words were not understood. The header says which solution is loaded and which "
                + "project the next command will act on, and 'Load' points the whole session - "
                + "console included - at another .sln, .slnx, .csproj or folder. The box completes "
                + "from the command catalog as you type, and shares its history with the prompt. "
                + "Typing exit there ends the chat session only, not the console. "
                + "Closing the tab ends the session. Alias: ui. Piped runs and CG_NO_WEB=1 keep "
                + "the console.",
            Example = "chat"
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
            Command = "run <file>",
            Description = "Runs a script of commands (.cgs) saved with save session or written by hand: every line, or none if one fails. "
                        + "Blank lines and lines starting with # are skipped. Only the commands the "
                        + "prompt runs one at a time, and build app blocks that end with approve or "
                        + "cancel, can be in a script; undo, batch, demo, chat, exit and the like are "
                        + "refused before anything runs. One undo reverses the whole run.",
            Example = "run library.cgs"
        },
        new()
        {
            Category = "Utility",
            Command = "save session <file> [replace]",
            Description = "Saves this session's commands that worked and changed something, as a script for run. "
                        + "A line a script cannot replay - a build app chosen on the page, a line only "
                        + "the AI could read, a relationship set from the offer, demo - is saved as a "
                        + "# note. Won't overwrite a file unless you add replace.",
            Example = "save session library.cgs"
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
            Description = "Exits the application. Alias: quit. In the chat page it ends the chat session only.",
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
            Description = "Compiles every .cs file in the project as one workspace and reports real errors, "
                        + "grouped by .csproj. With no project selected it uses the workspace. "
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
            Description = "Lists learned command signatures and their confidence, then the phrasings to review: "
                        + "each line that was not understood, paired with the next command that worked. "
                        + "Writes that list to .codegen/phrase-candidates.tsv. Aliases: list learning, view learning.",
            Example = "show learning"
        },
        new()
        {
            Category = "Learning",
            Command = "reset learning",
            Description = "Clears the learned command memory and the phrasings kept for review. "
                        + "Aliases: clear learning, forget learning.",
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
            Description = "Turns AI recognition back on in ai.json. On by default. Restart the CLI "
                        + "afterwards to load the model; it rebuilds its own index.",
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
            Description = "Rebuilds the phrase index from the current commands. The CLI does this "
                        + "itself at startup; run it only when 'ai status' reports REINDEX REQUIRED.",
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
            Description = "Describes a system in plain English. Classes are found from a local Ollama model, "
                        + "then the Wikipedia store, then the 25 built-in domains, then Wikipedia online. "
                        + "Interactively this opens a "
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
                        + "workflow page in the browser drawn as ten connected stages, ending "
                        + "in one that starts the application it just built. The stages are the "
                        + "navigation: each box shows the answer it holds, the box being answered "
                        + "carries the step forward and an arrow back one stage, the next box "
                        + "lights up when that step is ready, and clicking a finished box goes back "
                        + "to change it — there are no Back and Next buttons. Generate and Run stay "
                        + "disabled until the project has actually been written. Review classes "
                        + "is where the model is corrected: every class can be renamed or removed "
                        + "and every property renamed, retyped or deleted, on the card itself; a class "
                        + "or a field can be added, and a suggestion promoted to a class. "
                        + "Output, the stage after it, draws how the classes connect and "
                        + "states in one table the "
                        + "screen each class will get — master, transaction, or the transaction a "
                        + "line is edited inside — and lets you change it before anything is "
                        + "written. Entry Template, the stage after that, chooses how a "
                        + "business-shell entry form arranges its fields, when the target is wpf "
                        + "and the layout is business. A long step "
                        + "reports its progress as it works, "
                        + "and changing the description or the data source asks first, because "
                        + "it discards the model that was discovered from the old one; Cancel asks "
                        + "the same way once there is an answer to lose. The panel that opens holds "
                        + "the keyboard and Enter takes the step it offers — never Generate or Run, "
                        + "which stay deliberate clicks. A named data file is summarised — how "
                        + "many rows and columns — before anything is built from it; a step that "
                        + "fails says why and stays clickable rather than ending the run; the "
                        + "application the last stage starts can be stopped from the page; and the "
                        + "console signs off with the `build app:` line that would do the same run "
                        + "again. Two tabs on "
                        + "the same workflow stay in step, and closing the last one ends the "
                        + "command. Piped runs skip both the page and the wizard and go straight "
                        + "to the review loop; CG_NO_WEB=1 keeps the console wizard.",
            Example = "build app"
        },
        new()
        {
            Category = "Build App",
            Command = "build app (with an unfinished build in the workspace)",
            Description = "Offers the build you left. Closing the browser tab no longer "
                        + "loses the work: the answers and the reviewed model - renames, "
                        + "added and removed fields, promoted suggestions, the "
                        + "master/transaction split - are saved to "
                        + ".codegen/build-in-progress.json after every change, and the next "
                        + "interactive run in that workspace offers to resume, start fresh "
                        + "or discard. Enter starts fresh and deletes nothing - that fresh "
                        + "run is not saved as you go, so the build you left is still offered "
                        + "next time. Your DATA is "
                        + "not saved, only where it came from, so the source is re-read on "
                        + "resume; a file that has moved, or a SQL connection string (which "
                        + "is never saved), opens the data stage instead of failing. A "
                        + "finished or cancelled build clears the snapshot; one abandoned by "
                        + "closing the tab keeps it. Piped runs neither write one nor read "
                        + "one.",
            Example = "build app"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from <format> <location>",
            Description = "Builds the model from existing data with no prompts when piped; "
                        + "interactively the file is already answered and the workflow page opens "
                        + "on what is left. Formats: csv, txt, json, excel, sql (also typed as tsv, xlsx, "
                        + "sqlserver and the like). A relative path is looked for in the workspace first. For sql the "
                        + "location is a SQL Server connection string rather than a path. Add "
                        + "'normalize' to apply every normalization change the data needs.",
            Example = "build app: order tracking from csv D:\\data\\orders.csv normalize"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from sql \"<connection>\" [tables <A,B>]",
            Description = "Builds the model from a SQL Server database. Interactively the "
                        + "Data stage lists the tables with their column and row counts and "
                        + "you tick the ones you want; nothing is read until you take the "
                        + "step, and up to 100,000 rows of each table you ticked are then "
                        + "read - a bigger table is read that far and the build says so, and "
                        + "any rule offered from it says it came from the rows read. The rows "
                        + "SEEDED into the generated project are capped per class, and the "
                        + "build says that too. "
                        + "'tables "
                        + "A,B,C' names them for a piped run, and naming none reads them all. "
                        + "Column types come from the database instead of being inferred from "
                        + "the text, and declared foreign keys become relationships rather "
                        + "than being guessed from column names. A bigint column becomes int, "
                        + "a uniqueidentifier becomes string and a time becomes string (hh:mm:ss); "
                        + "each is reported. Composite "
                        + "foreign keys are skipped. Any password in the connection string is "
                        + "masked wherever the run is echoed back, so the replay line needs "
                        + "the password typed in again before it will run.",
            Example = "build app: a shop from sql \"Server=.;Database=Shop\" tables Customer"
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
                        + "(CodisLtd.Shop), leaving the project and assembly names alone, and "
                        + "namespace none clears a remembered prefix; "
                        + "layout tabs|sidebar|master-detail|dashboard|business chooses the shape "
                        + "of the window (wpf and winforms only); business is the default and is "
                        + "the shell with a dashboard home, a rail grouped into masters and "
                        + "transactions, and an editor for every record; the web app is always "
                        + "that shell, which is why naming a layout does not apply to it. "
                        + "entry-template classic|grouped|tabbed|two-column|split|ribbon chooses "
                        + "how a business-shell entry form arranges its fields (wpf only, for now, "
                        + "and only when the layout is business); grouped is the default. A "
                        + "layout you chose on an earlier interactive run is remembered in "
                        + ".codegen/app-profile.json and kept, default or not, so a run that "
                        + "is using a remembered one says so and names the file.",
            Example = "build app: a shop company \"Codis Ltd\" namespace CodisLtd theme dark"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [novalidation] [logging] [noseed] [nologin]",
            Description = "Chooses which optional layers are generated. 'novalidation' omits the "
                        + "per-model validators and the field, entity and business checks; the "
                        + "permission matrix is always generated, because turning validation off "
                        + "must not turn permissions off; 'logging' adds a "
                        + "logging layer (off by default); 'noseed' omits the sample seed rows "
                        + "(rows imported from your own data file are unaffected); 'nologin' "
                        + "omits the login screen, the user accounts and the Users screen, "
                        + "which an app with screens is given by default. Interactively "
                        + "these are asked by the 'Customize appearance and features?' step, "
                        + "which remembers your answers in .codegen/app-profile.json.",
            Example = "build app: a shop novalidation logging noseed nologin"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [transactions <A,B>] [masters <A,B>]",
            Description = "Overrules how the generator split your model into screens. It "
                        + "decides by itself which classes are transactions (a class with a "
                        + "foreign key AND either a date of its own or a transactional name) "
                        + "and which are masters (everything else); these name the ones it got "
                        + "wrong. Comma-joined with no spaces, or quoted. Naming a line entity "
                        + "as a master gives it a screen of its own instead of editing it "
                        + "inside its parent. Interactively you need name nothing: the workflow "
                        + "page's Output stage states the screen each class will get, in "
                        + "one table, and lets you change it there - which is the first "
                        + "point at which you know what the classes are called. A line names the "
                        + "transaction it is edited inside and offers one button to pull it out. "
                        + "Only the classes you move are recorded, so the run's command line "
                        + "names those and no others.",
            Example = "build app: a shop transactions Order,Invoice masters Region"
        },
        // The rest of this category is only valid at the review-loop prompt that
        // `build app:` opens, not at the main CodeGenerator prompt.
        new()
        {
            Category = "Build App",
            Command = "approve [target] [sqlite]",
            Description = "Review loop: generates the app using the type chosen at the start. "
                        + "Type ? or help there for its commands. "
                        + "Name a target to override it: wpf (default), winforms, console, webapi, "
                        + "blazor, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the "
                        + "default; the wpf and winforms targets are JSON-only). Alias: a.",
            Example = "approve webapi sqlite"
        },
        new()
        {
            Category = "Build App",
            Command = "approve (when the app already exists)",
            Description = "Offers to add only what is new. When the project folder already "
                        + "holds a generated app, the guard reads the classes already there "
                        + "and compares them with the model you just reviewed: it has 8 "
                        + "class(es), this model has 10, of which 2 are new. Answering a "
                        + "writes those 2 classes and the files that list the entities so "
                        + "their screens appear - and nothing else, so every other generated "
                        + "file is left byte-identical. The run says how many files it did "
                        + "not touch. A class that exists with DIFFERENT fields is named and "
                        + "left alone: adding will not reconcile it, overwriting would. Your "
                        + ".Custom.cs validator rules are never overwritten - which is also "
                        + "why an app generated before the validation pipeline needs one hand "
                        + "edit: its kept stub still declares the old ValidateCustom hook, so "
                        + "the run names each such file and the line to put in it, and the "
                        + "project will not compile until you change them. a is offered "
                        + "only when something really is new, Enter still cancels, and a "
                        + "piped run overwrites in silence as it always has.",
            Example = "approve wpf"
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
            Command = "renamefield <class> <old> <new>",
            Description = "Review loop: renames a field, keeping its type, where it came from "
                        + "and the rules proposed for it - which removing it and adding it "
                        + "again would lose.",
            Example = "renamefield Book Name Title"
        },
        new()
        {
            Category = "Build App",
            Command = "retype <class> <field> <type>",
            Description = "Review loop: changes a field's type to string, int, decimal, bool "
                        + "or DateTime, or to int?, decimal?, bool? or DateTime? for a field that may be "
                        + "empty (int? to int makes an empty value 0).",
            Example = "retype Book Pages int"
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
            Command = "rules",
            Description = "Review loop: lists every validation rule your data supports, numbered, "
                        + "with the evidence for each and whether it is accepted. None is applied "
                        + "until you accept it.",
            Example = "rules"
        },
        new()
        {
            Category = "Build App",
            Command = "accept <n>",
            Description = "Review loop: accepts a rule `rules` listed, by its number, or `accept "
                        + "all` for every one.",
            Example = "accept 2"
        },
        new()
        {
            Category = "Build App",
            Command = "unaccept <n>",
            Description = "Review loop: takes an accepted rule back, by number, or `unaccept all` "
                        + "for every one.",
            Example = "unaccept 2"
        },
        new()
        {
            Category = "Build App",
            Command = "order status <values> in <class>",
            Description = "Review loop: for a class with an accepted status proposal, states its "
                        + "discovered values IN ORDER, comma-joined and naming every one exactly "
                        + "once, so a rule can tell an earlier stage of a workflow from a later one.",
            Example = "order status Draft,Approved,Shipped in Order"
        },
        new()
        {
            Category = "Build App",
            Command = "final status <values> in <class>",
            Description = "Review loop: for a class with an accepted status proposal, marks which "
                        + "of its discovered values are FINAL — no further transition is expected "
                        + "from them.",
            Example = "final status Cancelled,Delivered in Order"
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

    /// <summary>
    /// What the tool does without being asked, which no command table can show.
    /// </summary>
    /// <remarks>
    /// Here rather than written out in each renderer, for the reason the command list itself is:
    /// help, the command reference and the manual appendix are three renderings of one catalog,
    /// and the manual's appendix has already drifted once from being maintained separately.
    /// <para>
    /// Two lines, because <see cref="GetOverview"/> is a one-screen answer and a third would be
    /// paid for out of the categories below it. The detail lives in the manual.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<StarterCommand> Unprompted { get; } =
    [
        // Both summaries fit an eighty-column console after the indent and the command column,
        // which leaves 56 characters. A test pins that; the first draft ran to 161 and wrapped.
        new("relationships", "offered after most commands, not class creation"),
        new("show relationships", "draws what is related to what, as boxes and arrows"),
    ];

    /// <summary>The heading those two sit under, in all three renderings.</summary>
    private const string UnpromptedHeading = "Set for you";

    /// <summary>
    /// The width every help rendering is written to.
    /// </summary>
    /// <remarks>
    /// Eighty, because that is the narrowest console anyone still uses and a line longer than the
    /// window is re-wrapped by the terminal at whatever column it runs out of — which is how the
    /// catalog came to be printed as ragged two-line entries with the second line starting
    /// mid-sentence under the left margin.
    /// </remarks>
    private const int ConsoleWidth = 80;

    /// <summary>
    /// One catalog entry: the command, then what it does, then how it is typed.
    /// </summary>
    /// <remarks>
    /// The command gets a line of its own rather than a column. Entries here run to 45 characters
    /// (<c>create class &lt;class name&gt; with properties</c>), which leaves a column layout
    /// about thirty for the description — so every description overflowed, and the overflow is
    /// what made the output look broken. Stacked and indented, nothing wraps and the commands
    /// still make a single scannable edge.
    /// </remarks>
    private static IEnumerable<string> Entry(CommandInfo command)
    {
        yield return "  " + command.Command;

        foreach (var line in Wrap(command.Description, ConsoleWidth - 6))
            yield return "      " + line;

        // One line, never wrapped. An example is meant to be copied and typed back, and one
        // folded across two lines is one the reader has to reassemble by hand. The catalog keeps
        // them short enough to fit instead, which a test pins.
        yield return "      e.g. " + command.Example;
        yield return string.Empty;
    }

    /// <summary>A heading and the rule under it, sized to the heading rather than the screen.</summary>
    /// <remarks>
    /// A rule of eighty dashes under a five-letter word is the heaviest thing in the output and
    /// draws the eye to a separator rather than to the heading it belongs to.
    /// </remarks>
    private static IEnumerable<string> Heading(string text)
    {
        yield return text.ToUpperInvariant();
        yield return new string('-', Math.Max(text.Length, 12));
        yield return string.Empty;
    }

    /// <summary>
    /// Breaks text on whitespace so no line passes <paramref name="width"/>.
    /// </summary>
    /// <remarks>
    /// A word longer than the width is left alone rather than cut: it will be a command name or a
    /// path, and a broken one is worse than a long line.
    /// </remarks>
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();

        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');

            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return lines.Count == 0 ? [string.Empty] : lines;
    }

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
        lines.Add("  " + UnpromptedHeading);

        var unpromptedWidth = Unprompted.Max(s => s.Command.Length) + 2;

        foreach (var offer in Unprompted)
            lines.Add("    " + offer.Command.PadRight(unpromptedWidth) + offer.Summary);

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

        // The overview's "Set for you" heading, which is no category of the command table.
        if (TopicKey(UnpromptedHeading) == wanted)
            return string.Join(Environment.NewLine, UnpromptedSection()).TrimEnd();

        // Prefix, not equality: catalog entries carry their arguments ("approve [target]
        // [sqlite]"), and nobody types those to ask about one.
        var named = Commands
            .Where(c => TopicKey(c.Command).StartsWith(wanted, StringComparison.Ordinal))
            .ToList();

        // Then anywhere in the command: "business validation" is in each of add, show and
        // remove business validation, and none of them starts with it. Not for a short topic,
        // which would be found inside nearly every command.
        if (named.Count == 0 && wanted.Length >= 4)
            named = [.. Commands.Where(c => TopicKey(c.Command).Contains(wanted, StringComparison.Ordinal))];

        return named.Count == 0 ? null : Render(topic!.Trim(), named);
    }

    /// <summary>Case- and space-insensitive, so "Build App", "build app" and "buildapp" are one
    /// topic.</summary>
    private static string TopicKey(string? text)
        => new((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c))
            .Select(char.ToLowerInvariant).ToArray());

    private static string Render(string heading, IEnumerable<CommandInfo> commands)
    {
        var lines = new List<string>();

        lines.AddRange(Heading(heading));

        foreach (var command in commands.OrderBy(c => c.Command, StringComparer.Ordinal))
            lines.AddRange(Entry(command));

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    /// <summary>
    /// The "Set for you" section as <c>help all</c> and <c>help set for you</c> print it.
    /// </summary>
    private static List<string> UnpromptedSection()
    {
        var lines = new List<string>();
        lines.AddRange(Heading(UnpromptedHeading));

        foreach (var offer in Unprompted)
        {
            lines.Add("  " + offer.Command);

            foreach (var line in Wrap(offer.Summary, ConsoleWidth - 6))
                lines.Add("      " + line);

            lines.Add(string.Empty);
        }

        // Said here and not in the overview, which has no room for it: a scripted run is never
        // asked anything, and someone whose pipeline never shows the offer needs to know that is
        // deliberate rather than broken.
        foreach (var line in Wrap(
                     "A piped or scripted run is never asked: with nobody at the keyboard there "
                     + "is nobody to answer, so the offer does not appear at all.",
                     ConsoleWidth - 2))
        {
            lines.Add("  " + line);
        }

        lines.Add(string.Empty);
        return lines;
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
            "Smart Code Generator — every command",
            string.Empty
        };

        lines.AddRange(UnpromptedSection());

        foreach (var group in Commands
                     .OrderBy(c => c.Category)
                     .GroupBy(c => c.Category))
        {
            lines.AddRange(Heading($"{group.Key} ({group.Count()})"));

            foreach (var command in group.OrderBy(c => c.Command))
                lines.AddRange(Entry(command));
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
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

        // Not every behaviour is a command. The relationship offer is raised by the tool itself
        // after a command finishes, so it has no row in any table above and would otherwise be
        // invisible to anyone reading only this file. Rendered from the same two entries `help`
        // shows, so the two cannot drift.
        lines.Add($"## {UnpromptedHeading}");
        lines.Add(string.Empty);

        foreach (var offer in Unprompted)
            lines.Add($"- `{Cell(offer.Command)}` — {Cell(offer.Summary)}");

        lines.Add(string.Empty);
        lines.Add(
            "A piped or scripted run is never asked. See \"Relationships between classes\" in "
            + "the user manual for what a relationship is and what each answer writes.");
        lines.Add(string.Empty);

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// The same catalog as HTML, for the user manual's command-reference appendix.
    /// </summary>
    /// <remarks>
    /// The appendix was maintained by hand and fell behind: it offered four window layouts after a
    /// fifth became the default, and listed the optional-layer options without <c>nologin</c>. Two
    /// copies of one catalog is one copy too many, so it is generated from the same list
    /// <see cref="GetMarkdownReference"/> uses, in the same order, and a test keeps the committed
    /// HTML in step.
    /// <para>
    /// Only the tables. The chapter heading and its introduction are prose and stay in the source
    /// document, between the markers the test looks for.
    /// </para>
    /// </remarks>
    public static string GetHtmlReference()
    {
        var lines = new List<string>();

        foreach (var group in Commands
                     .OrderBy(c => c.Category)
                     .GroupBy(c => c.Category))
        {
            lines.Add($"<h3>{Html(group.Key)}</h3>");
            lines.Add("<table>");
            lines.Add("<tr><th>Command</th><th>Description</th><th>Example</th></tr>");

            foreach (var command in group.OrderBy(c => c.Command))
                lines.Add(
                    $"<tr><td><code>{Html(command.Command)}</code></td>"
                    + $"<td>{Html(command.Description)}</td>"
                    + $"<td><code>{Html(command.Example)}</code></td></tr>");

            lines.Add("</table>");
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Escapes a value for an HTML table cell.
    ///
    /// Every command that documents an argument carries angle brackets, and Word's HTML
    /// converter reads <c>&lt;class name&gt;</c> as a tag and drops it — the text simply is
    /// not in the .docx, with nothing in the conversion log to say so.</summary>
    private static string Html(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&#x27;", StringComparison.Ordinal);

    /// <summary>Escapes a value for a Markdown table cell.
    ///
    /// A bare <c>|</c> ends the cell, so a command documenting alternatives —
    /// <c>ai mode &lt;retrieval|generative|hybrid&gt;</c> — silently renders as five
    /// columns and breaks the whole row. Backticks do not protect it; the escape is
    /// still required inside a code span.</summary>
    private static string Cell(string value) => value.Replace("|", @"\|");
}