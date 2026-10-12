namespace CodeGenerator.Web.Workflow;

/// <summary>One block of a help page: a heading and the lines under it.</summary>
/// <param name="Heading">What the block is about, for example "What you see".</param>
/// <param name="Items">The lines. Each is one fact or one instruction.</param>
/// <param name="Numbered">True when the order matters, so the page draws a numbered list.</param>
public sealed record HelpSection(string Heading, IReadOnlyList<string> Items, bool Numbered = false);

/// <summary>What the help page says about one stage.</summary>
/// <param name="Summary">What the stage is for, and why it comes where it does.</param>
/// <param name="Sections">The blocks under the summary, in reading order.</param>
/// <param name="Next">What the next stage is.</param>
public sealed record StageHelpEntry(string Summary, IReadOnlyList<HelpSection> Sections, string Next);

/// <summary>
/// The text behind each stage's help icon, and the address that icon opens.
/// </summary>
/// <remarks>
/// <see cref="For"/> is a switch with no default arm, so a stage added to <see cref="BuildStage"/>
/// with no help is warning CS8509, which CI's <c>-warnaserror</c> build refuses. Only CS8524 (an
/// unnamed number) is turned off; such a value throws <c>SwitchExpressionException</c>.
/// The address carries the enum name, lower-cased, which is also what a saved snapshot holds.
/// <para>
/// The wording names the controls as the stage draws them — "Write it", "Run it now", "own
/// screen" — because a help page that says "click Generate" over a button called "Write it" is
/// worse than none. A change to a label in a stage panel needs the same change here.
/// </para>
/// </remarks>
public static class StageHelp
{
    public static string AddressOf(BuildStage stage) => $"/help/{stage.ToString().ToLowerInvariant()}";

    /// <summary>The stage a name in an address stands for, or null. A number is not a stage.</summary>
    public static BuildStage? Parse(string? name)
        => Enum.GetValues<BuildStage>().Cast<BuildStage?>()
            .FirstOrDefault(s => string.Equals(s.ToString(), name, StringComparison.OrdinalIgnoreCase));

    // CS8524 (a number that names no stage) is the only warning turned off: CS8509 (a NAMED stage
    // with no arm) must stay, because it is what refuses a new stage that has no help.
#pragma warning disable CS8524
    public static StageHelpEntry For(BuildStage stage) => stage switch
    {
        BuildStage.Describe => new(
            "This is the first question. You say in plain English what the application is for. "
            + "The tool uses your words to work out the classes (the things the application keeps "
            + "records of) in the Discover stage.",
            [
                new("What you see",
                [
                    "One text box. Press Enter in the box to start a new line. A description can have more than one sentence.",
                    "A Cancel button. It ends the build. If you have already given answers, it asks you to confirm first.",
                ]),
                new("What to do",
                [
                    "Type what the application does. For example: a library that lends books to members.",
                    "Name the things and the actions. Words such as books, members and lends help the tool find classes.",
                    "Click the Continue arrow in the stage box, or press Enter outside the text box.",
                ], Numbered: true),
                new("Good to know",
                [
                    "You can leave the box empty. Do this when you will give a data file in the Data stage. The file then decides the classes.",
                    "If you come back here and change the text after discovery, the page asks first. The classes found from the old text are lost.",
                    "A short description gives few classes. You can add more in Review classes, so a first try does not need to be perfect.",
                ]),
            ],
            "Application Type: choose which kind of application to build."),

        BuildStage.AppType => new(
            "You choose the kind of program the tool writes. The choice decides the project files, "
            + "the screens, and how you start the result.",
            [
                new("The choices",
                [
                    "WPF (Windows desktop): a Windows application with a dashboard, a grouped list of screens, and an editor for each record. This is the default.",
                    "Windows Desktop (WinForms): the same kind of application, built with Windows Forms.",
                    "ASP.NET Web API: a service with REST endpoints for each class. It has no screens.",
                    "Web Application (Blazor Server): the same business screens, in a web browser. Each record has its own address, for example /order/7.",
                    "Console: a text application with a menu to create, read, update and delete records.",
                    "Class Library: the shared classes only. It has no entry point, so the Run stage has nothing to start.",
                    "Worker Service (background): a background service that works with the repositories.",
                ]),
                new("What to do",
                [
                    "Select one option. The first option is selected for you.",
                    "Click the Continue arrow in the stage box.",
                ], Numbered: true),
                new("Good to know",
                [
                    "Only WPF and Windows Desktop have window layouts, and only WPF has entry templates. You set these in the Look and Entry Template stages.",
                    "SQLite storage is not available for WPF and Windows Desktop. The Generate stage hides that choice for them.",
                    "A changed choice does not remove the classes you found. It changes only what the tool writes.",
                ]),
            ],
            "Data: choose where the model comes from."),

        BuildStage.Data => new(
            "Here you say if the classes come from your own data or from discovery. If you give data, "
            + "the data is the source of truth. The classes, the property types and the sample rows come from it.",
            [
                new("What you see",
                [
                    "Two options: \"No data — discover the domain from the web\" and \"I have a file\".",
                    "After you select \"I have a file\": a Format list (csv, json, txt, excel, sql), then a Path box, or a Connection box for sql.",
                    "A drop area. You can drop a file there and the tool copies it into the workspace cache.",
                ]),
                new("To read a file",
                [
                    "Select \"I have a file\" and choose the Format.",
                    "Type the Path, or drop the file in the drop area.",
                    "Read the line that appears under the path, for example: orders.csv: orders — 240 rows, 12 columns. Make sure it is the file you want.",
                    "Click the Continue arrow.",
                ], Numbered: true),
                new("To read a SQL Server database",
                [
                    "Choose the format sql and type the Connection string.",
                    "Click connect. The page lists the tables with the number of columns and rows in each.",
                    "Tick the tables you want. Nothing is read until you continue.",
                    "The tool reads up to 100,000 rows of each ticked table, and it says so when a table is bigger. Column types and foreign keys come from the database.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A password in a connection string is never saved, and it is masked in the command that the Output stage shows.",
                    "Choose \"No data\" to build from your description only.",
                    "If you change the source after discovery, the page asks first, because the model from the old source is lost.",
                ]),
            ],
            "Look: set the branding, the theme and the optional features."),

        BuildStage.Look => new(
            "Look sets how the application is branded and which extra features it has. "
            + "Every setting is optional. Skip the stage if you want the defaults.",
            [
                new("What you see",
                [
                    "One checkbox: \"Customize appearance and features\". All the settings are hidden until you tick it.",
                    "If settings from an earlier build still apply, the page lists them and offers \"Use the defaults instead\".",
                ]),
                new("The settings",
                [
                    "Layout (WPF and Windows Desktop only): business is the default. Others are tabs, sidebar, master-detail and dashboard.",
                    "Company: put in the project file and in a copyright line at the top of each generated C# file.",
                    "Theme: none, light or dark. This applies to the desktop applications.",
                    "Accent colour: a colour as #rrggbb. A wrong value is ignored.",
                    "Icon path: a .ico or .png file that becomes the application icon.",
                    "Window title: a pattern. {company} and {app} are replaced, so \"{company} - {app}\" gives Codis Ltd - Shop.",
                    "Namespace prefix: puts the code under your own namespace, for example CodisLtd.Shop.Models.",
                    "Validation layer, Logging, Sample seed data, Require a login: tick to add each one.",
                ]),
                new("What to do",
                [
                    "To keep the defaults, leave the checkbox empty and click the Continue arrow.",
                    "To change a setting, tick \"Customize appearance and features\".",
                    "Fill in only the settings you want to change.",
                    "Click the Continue arrow.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A web application has no window, so it ignores the layout.",
                    "Seed data adds sample rows. Rows from your own data file always come with the project.",
                    "Your answers are remembered in .codegen/app-profile.json and used again in the next build.",
                ]),
            ],
            "Discover: the tool works out the classes."),

        BuildStage.Discover => new(
            "The tool builds the model: the classes and their properties. It works from your "
            + "description, from your data, or from both. This stage can take a long time.",
            [
                new("What you see",
                [
                    "While it works: a progress list with a seconds counter. It shows the classes done and the classes still to do.",
                    "After it reads a data file: a line that says what was read, and a list of suggested clean-up changes (normalization). Each change has a checkbox and a Why part that shows the evidence.",
                    "When you come back to this stage: the number of classes found, and a button to \"Discover again\" or \"Read the file again\".",
                ]),
                new("What to do",
                [
                    "For a file: read the suggested changes. Tick the ones you want. Each change shows its proposal. Open Why to see the evidence.",
                    "For discovery: wait. A local model on a CPU can take several minutes. The seconds counter shows that it still works.",
                    "Click the Continue arrow when the stage allows it.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A step that fails shows why, and the stage stays open. Click an earlier stage to change the input, then try again.",
                    "If you leave and return, the tool keeps the model. Use the \"again\" button only when you want to lose it and start over.",
                    "Discovery names are often rough. You correct them in the next stage.",
                ]),
            ],
            "Review classes: read the classes and correct them."),

        BuildStage.Review => new(
            "This is where you correct the model, before anything is written. Each class is one card. "
            + "You can change everything on a card in place. Nothing is written to disk until later stages.",
            [
                new("What you see",
                [
                    "One card for each class. The header has the class name, a role chip when the tool knows the role, where the class came from, a \"why\" button and a \"remove\" button.",
                    "Under the header: the properties. Each has a name box, a type list, and a button to remove it.",
                    "At the bottom of a card: a new property row with a type list and an \"add field\" button.",
                    "An \"Also found\" area with suggested classes. Each has a + button.",
                    "A \"new class\" box with an \"add class\" button.",
                ]),
                new("What you can change",
                [
                    "Rename a class: type in the name box on the card.",
                    "Remove a class: click \"remove\".",
                    "Rename or retype a property: change its name box or its type list.",
                    "Add a property: type a name, choose a type, click \"add field\".",
                    "Add a class: type the name in \"new class\" and click \"add class\".",
                    "Promote a suggestion: click its + button to make it a class.",
                    "Accept or reject proposed rules on a card by their checkboxes, for example a status order.",
                ]),
                new("What to do",
                [
                    "Read each card. Check the class names and the properties.",
                    "Fix what is wrong with the changes above. Use \"why\" on a class you do not understand.",
                    "Add the classes and properties that are missing.",
                    "Click the Continue arrow when the model is right.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A change the editor cannot accept is refused and the old text returns. A message below the cards says why.",
                    "A suggestion is a name that the tool saw but did not find fields for. It is offered, not deleted.",
                    "The \"why\" button opens the evidence for the class, so you can decide if it belongs.",
                    "Your changes are saved after each edit. If you close the tab, the next run offers to continue.",
                ]),
            ],
            "Output: see how the classes connect and which ones get a screen."),

        BuildStage.Output => new(
            "Output is the last look before the tool writes the project. It shows how the classes "
            + "connect, which screen each class gets, and the full command for this run.",
            [
                new("What you see",
                [
                    "A relationship diagram. It shows which classes link to which.",
                    "A \"Screens\" table with one row for each class and the screen it gets.",
                    "A \"Will generate\" box: the application type and your Look settings.",
                    "\"The same run as a command\": one line that repeats this whole run.",
                ]),
                new("The screen kinds",
                [
                    "master: a list of records with its own screen, for example Customer or Product.",
                    "transaction: a document, for example an Order. It has a foreign key and usually a date.",
                    "lines of <class>: a line item, for example an Order line. It is edited inside its parent. The button \"own screen\" gives it a screen of its own.",
                ]),
                new("What to do",
                [
                    "Read the diagram. Check that the links are right.",
                    "Read the Screens table. The tool guessed each kind from the model. Change a master or a transaction with its list.",
                    "Read the summary sentence under the table.",
                    "Click the Continue arrow.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A line item cannot be changed to a transaction. The only change is \"own screen\".",
                    "Only classes you change are recorded in the command.",
                    "Copy the command to repeat the build later, or to run it without the page. A SQL password is masked, so you type it again.",
                ]),
            ],
            "Entry Template: choose how entry forms arrange their fields."),

        BuildStage.EntryTemplate => new(
            "An entry template decides how the form for one record arranges its fields. "
            + "Output decides which classes get a master or transaction screen. This stage decides how those screens look.",
            [
                new("When it applies",
                [
                    "Only when the application type is WPF and the layout is business.",
                    "In all other cases the page says \"Only available for a WPF application using the business layout.\" and offers no choice. Continue to the next stage.",
                ]),
                new("The templates",
                [
                    "classic: one flat column of fields, with no grouping.",
                    "grouped: fields in titled panels, three columns wide. This is the default.",
                    "tabbed: fields in titled panels, one panel in each tab.",
                    "two-column: fields in titled panels, two columns wide.",
                    "split: the record list and its editor together on one screen, as the master screen does.",
                    "ribbon: the action bar as a row of slimmer, taller buttons.",
                ]),
                new("What to do",
                [
                    "Look at the picture for each template. Click a picture to see it larger. Press Escape or click outside to close it.",
                    "Select the template you want.",
                    "Click the Continue arrow.",
                ], Numbered: true),
            ],
            "Generate: the tool writes the project and builds it."),

        BuildStage.Generate => new(
            "Generate writes the project to disk and builds it with the compiler. "
            + "This is the first stage that changes files, so the main button here is a deliberate click.",
            [
                new("What to do",
                [
                    "Read the name in \"Ready to write <name>\". Change the Name box if you want a different name.",
                    "Choose Persistence: JSON (a file for each class, no database) or SQLite (a database through EF Core). SQLite is not offered for WPF and Windows Desktop.",
                    "Read the notice if one shows, for example when the layout does not apply to the type.",
                    "Click \"Write it\". Click Cancel instead to stop the build.",
                    "Watch the progress list and the build log. The page shows the project folder after it is written.",
                ], Numbered: true),
                new("If the folder already exists",
                [
                    "The page says what is there and asks you to choose.",
                    "\"Add the new classes\": add only the classes that are not there. This shows only if there are new classes.",
                    "\"Overwrite it\": replace the project.",
                    "\"Use a different name\": go back and change the Name.",
                ]),
                new("While it works",
                [
                    "A progress list and a seconds counter show the steps.",
                    "The build log from the compiler appears as it runs.",
                    "When it ends, the page lists what was written and the result of the build.",
                ]),
                new("If the build fails",
                [
                    "The page lists the errors and offers to fix them. Accept to let the tool try. Decline to keep the files as they are.",
                    "The tool fixes known problems with rules first. It uses the local model only when the rules cannot fix an error.",
                    "After a fix, the project is built again and the new result replaces the old one.",
                ]),
                new("Good to know",
                [
                    "The Run stage opens only after the project is written. If nothing was written, click Done to end.",
                    "If a copy of the application still runs, stop it before you build again. A running application locks its own files.",
                ]),
            ],
            "Run: start the finished application."),

        BuildStage.Run => new(
            "A project that compiles is not the same as one that works. Run starts the finished "
            + "application, so you see it work.",
            [
                new("What you see",
                [
                    "The project folder and a line that says it compiled.",
                    "A \"Next steps\" list: what to do with the project after this page.",
                    "Buttons: \"Run it now\" (it changes to \"Start it again\" after the first start), \"Stop it (process <number>)\" while it runs, and \"Done\".",
                ]),
                new("What to do",
                [
                    "Click \"Run it now\". The application starts as a separate process.",
                    "Try the application.",
                    "Click \"Stop it\" when you finish, or leave it running. It keeps running after this page closes.",
                    "Click \"Done\" to end the workflow. The console then prints a report and the command for the same run.",
                ], Numbered: true),
                new("Good to know",
                [
                    "A class library has no entry point and a project that failed to build cannot start. The page says why instead of offering the button.",
                    "Stop the application before you build the same project again. Otherwise the build fails because the files are in use.",
                    "To change the application, click an earlier stage. The changed answer runs again from that stage.",
                ]),
            ],
            "This is the last stage."),
    };
#pragma warning restore CS8524
}
