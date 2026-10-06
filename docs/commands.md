# Command Reference

<!-- Generated from CommandDictionary.GetMarkdownReference(). Do not edit by hand.
     Regenerate: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`. -->

## AI

| Command | Description | Example |
| --- | --- | --- |
| `ai disable` | Turns AI recognition off. Command recognition returns to the rule-based path exactly as it behaves with AI absent. | `ai disable` |
| `ai enable` | Turns AI recognition back on in ai.json. On by default. Restart the CLI afterwards to load the model; it rebuilds its own index. | `ai enable` |
| `ai mode <retrieval\|generative\|hybrid>` | Sets how AI reads unrecognized input. Only retrieval works today; generative and hybrid are declined until a later phase. | `ai mode retrieval` |
| `ai reindex` | Rebuilds the phrase index from the current commands. The CLI does this itself at startup; run it only when 'ai status' reports REINDEX REQUIRED. | `ai reindex` |
| `ai status` | Reports whether AI command recognition is on, the model and index in use, both confidence gates, and how many decisions have been logged. Plain 'ai' does the same. | `ai status` |
| `ai why` | Explains the last AI reading: what you typed, how it was read, the supporting examples, and whether you accepted it. | `ai why` |

## Analysis

| Command | Description | Example |
| --- | --- | --- |
| `analyze class <class name>` | Analyzes a specific class. | `analyze class Employee` |
| `analyze project` | Analyzes the current project. | `analyze project` |
| `validate project` | Compiles every .cs file in the project as one workspace and reports real errors. Aliases: check project, compile project. | `validate project` |

## Build App

| Command | Description | Example |
| --- | --- | --- |
| `accept <n>` | Review loop: accepts a rule `rules` listed, by its number, or `accept all` for every one. | `accept 2` |
| `add <name>` | Review loop: adds a class to the candidate model. The name keeps the shape you type, so a compound survives; several words become one class name. | `add cash register` |
| `approve (when the app already exists)` | Offers to add only what is new. When the project folder already holds a generated app, the guard reads the classes already there and compares them with the model you just reviewed: it has 8 class(es), this model has 10, of which 2 are new. Answering a writes those 2 classes and the files that list the entities so their screens appear - and nothing else, so every other generated file is left byte-identical. The run says how many files it did not touch. A class that exists with DIFFERENT fields is named and left alone: adding will not reconcile it, overwriting would. Your .Custom.cs validator rules are never overwritten - which is also why an app generated before the validation pipeline needs one hand edit: its kept stub still declares the old ValidateCustom hook, so the run names each such file and the line to put in it, and the project will not compile until you change them. a is offered only when something really is new, Enter still cancels, and a piped run overwrites in silence as it always has. | `approve wpf` |
| `approve [target] [sqlite]` | Review loop: generates the app using the type chosen at the start. Name a target to override it: wpf (default), winforms, console, webapi, blazor, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the default; the wpf and winforms targets are JSON-only). Alias: a. | `approve webapi sqlite` |
| `build app` | Starts the same flow with nothing filled in. Interactively it opens a workflow page in the browser drawn as ten connected stages, ending in one that starts the application it just built. The stages are the navigation: each box shows the answer it holds, the box being answered carries the step forward and an arrow back one stage, the next box lights up when that step is ready, and clicking a finished box goes back to change it — there are no Back and Next buttons. Generate and Run stay disabled until the project has actually been written. Review classes is where the model is corrected: every class can be renamed or removed and every property renamed, retyped or deleted, on the card itself. Output, the stage after it, draws how the classes connect and states in one table the screen each class will get — master, transaction, or the transaction a line is edited inside — and lets you change it before anything is written. Entry Template, the stage after that, chooses how a business-shell entry form arranges its fields, when the target is wpf and the layout is business. A long step reports its progress as it works, and changing the description or the data source asks first, because it discards the model that was discovered from the old one; Cancel asks the same way once there is an answer to lose. The panel that opens holds the keyboard and Enter takes the step it offers — never Generate or Run, which stay deliberate clicks. A named data file is summarised — how many rows and columns — before anything is built from it; a step that fails says why and stays clickable rather than ending the run; the application the last stage starts can be stopped from the page; and the console signs off with the `build app:` line that would do the same run again. Two tabs on the same workflow stay in step, and closing the last one ends the command. Piped runs skip both the page and the wizard and go straight to the review loop; CG_NO_WEB=1 keeps the console wizard. | `build app` |
| `build app (with an unfinished build in the workspace)` | Offers the build you left. Closing the browser tab no longer loses the work: the answers and the reviewed model - renames, added and removed fields, promoted suggestions, the master/transaction split - are saved to .codegen/build-in-progress.json after every change, and the next interactive run in that workspace offers to resume, start fresh or discard. Enter starts fresh and deletes nothing - that fresh run is not saved as you go, so the build you left is still offered next time. Your DATA is not saved, only where it came from, so the source is re-read on resume; a file that has moved, or a SQL connection string (which is never saved), opens the data stage instead of failing. A finished or cancelled build clears the snapshot; one abandoned by closing the tab keeps it. Piped runs neither write one nor read one. | `build app` |
| `build app: <description>` | Describes a system in plain English. Interactively this opens a workflow page in the browser — the console prints the address and waits — where the rest of the build happens: application type, data, look, domain discovery, model review, generate-and-build, then running the finished application. Every step that takes a while says what it is doing and how long it has been doing it, and the compiler's output appears line by line as it builds. Piped runs are unchanged, and CG_NO_WEB=1 keeps the console wizard. | `build app: a library that lends books to members` |
| `build app: <description> [customization options]` | Customizes the generated application. Options go LAST on the line, after any data source: company "<name>" sets the company (used in the project metadata, the window title and a copyright header); theme light\|dark and accent #rrggbb colour the desktop front ends; icon <path> supplies a .ico or .png; title "<pattern>" overrides the window title, where {company} and {app} are substituted; namespace <prefix> puts the generated code under that namespace root (CodisLtd.Shop), leaving the project and assembly names alone; layout tabs\|sidebar\|master-detail\|dashboard\|business chooses the shape of the window (wpf and winforms only); business is the default and is the shell with a dashboard home, a rail grouped into masters and transactions, and an editor for every record; the web app is always that shell, which is why naming a layout does not apply to it. entry-template classic\|grouped\|tabbed\|two-column\|split\|ribbon chooses how a business-shell entry form arranges its fields (wpf only, for now, and only when the layout is business); grouped is the default. A layout you chose on an earlier interactive run is remembered in .codegen/app-profile.json and kept, default or not, so a run that is using a remembered one says so and names the file. | `build app: a shop company "Codis Ltd" namespace CodisLtd theme dark` |
| `build app: <description> [novalidation] [logging] [noseed] [nologin]` | Chooses which optional layers are generated. 'novalidation' omits the per-model validators and the field, entity and business checks; the permission matrix is always generated, because turning validation off must not turn permissions off; 'logging' adds a logging layer (off by default); 'noseed' omits the sample seed rows (rows imported from your own data file are unaffected); 'nologin' omits the login screen, the user accounts and the Users screen, which an app with screens is given by default. Interactively these are asked by the 'Customize appearance and features?' step, which remembers your answers in .codegen/app-profile.json. | `build app: a shop novalidation logging noseed nologin` |
| `build app: <description> [transactions <A,B>] [masters <A,B>]` | Overrules how the generator split your model into screens. It decides by itself which classes are transactions (a class with a foreign key AND either a date of its own or a transactional name) and which are masters (everything else); these name the ones it got wrong. Comma-joined with no spaces, or quoted. Naming a line entity as a master gives it a screen of its own instead of editing it inside its parent. Interactively you need name nothing: the workflow page's Output stage states the screen each class will get, in one table, and lets you change it there - which is the first point at which you know what the classes are called. A line names the transaction it is edited inside and offers one button to pull it out. Only the classes you move are recorded, so the run's command line names those and no others. | `build app: a shop transactions Order,Invoice masters Region` |
| `build app: <description> from <format> <location>` | Builds the model from existing data with no prompts when piped; interactively the file is already answered and the workflow page opens on what is left. Formats: csv, txt, json, excel, sql. For sql the location is a SQL Server connection string rather than a path. Add 'normalize' to apply every normalization change the data needs. | `build app: order tracking from csv D:\data\orders.csv normalize` |
| `build app: <description> from sql "<connection>" [tables <A,B>]` | Builds the model from a SQL Server database. Interactively the Data stage lists the tables with their column and row counts and you tick the ones you want; nothing is read until you take the step, and up to 100,000 rows of each table you ticked are then read - a bigger table is read that far and the build says so, and any rule offered from it says it came from the rows read. The rows SEEDED into the generated project are capped per class, and the build says that too. 'tables A,B,C' names them for a piped run, and naming none reads them all. Column types come from the database instead of being inferred from the text, and declared foreign keys become relationships rather than being guessed from column names. A bigint column becomes int and a uniqueidentifier becomes string; both are reported. Composite foreign keys are skipped. Any password in the connection string is masked wherever the run is echoed back, so the replay line needs the password typed in again before it will run. | `build app: a shop from sql "Server=.;Database=Shop" tables Customer` |
| `cancel` | Review loop: abandons the model without generating anything. Alias: c. | `cancel` |
| `field <class> <name>[:<type>]` | Review loop: adds a field to a class. Omit the type to have it inferred; explicit types are string, int, decimal, bool or DateTime. | `field Book Title:string` |
| `final status <values> in <class>` | Review loop: for a class with an accepted status proposal, marks which of its discovered values are FINAL — no further transition is expected from them. | `final status Cancelled,Delivered in Order` |
| `order status <values> in <class>` | Review loop: for a class with an accepted status proposal, states its discovered values IN ORDER, comma-joined and naming every one exactly once, so a rule can tell an earlier stage of a workflow from a later one. | `order status Draft,Approved,Shipped in Order` |
| `remove <name>` | Review loop: removes a class from the candidate model. | `remove Reservation` |
| `rename <old> <new>` | Review loop: renames a class in the candidate model. | `rename Member Borrower` |
| `renamefield <class> <old> <new>` | Review loop: renames a field, keeping its type, where it came from and the rules proposed for it - which removing it and adding it again would lose. | `renamefield Book Name Title` |
| `retype <class> <field> <type>` | Review loop: changes a field's type to string, int, decimal, bool or DateTime. | `retype Book Pages int` |
| `rules` | Review loop: lists every validation rule your data supports, numbered, with the evidence for each and whether it is accepted. None is applied until you accept it. | `rules` |
| `unaccept <n>` | Review loop: takes an accepted rule back, by number, or `unaccept all` for every one. | `unaccept 2` |
| `unfield <class> <name>` | Review loop: removes a field from a class. | `unfield Book Title` |
| `why [name]` | Review loop: explains what discovery decided. Bare, it reports every decision grouped by outcome; with a name, it shows that name's whole trail — the stage, the reason and the evidence behind each step. | `why Cash register` |

## Class

| Command | Description | Example |
| --- | --- | --- |
| `<class name> inherits <base class name>` | Adds a base class to a class, keeping any existing interfaces. Aliases: extends, derives from. | `Order inherits AuditableBase` |
| `create class` | Creates a new class. | `create class Employee` |
| `create class <class name> with properties` | Creates a new class with the specified properties. | `create class Employee with properties EmpCode int, Name string` |
| `delete class` | Moves a class to DeletedClasses. | `delete class Employee` |
| `list classes` | Lists all classes in the current project. | `list classes` |
| `list classes in <project name>` | Lists all classes in a project. | `list classes in EmployeeManagement` |
| `list deleted classes` | Lists all deleted classes. | `list deleted classes` |
| `rename class` | Rename a class. | `rename class Employee to Staff` |
| `restore class` | Restores a deleted class. | `restore class Employee` |
| `show class` | Displays the class source code. | `show class Employee` |
| `show relationships` | Draws the classes in the workspace and what each one is related to, as boxes and arrows on a page in the browser. A relationship is a pair - an int <Target>Id and a property typed as that class - so a class carrying only one half of it is drawn apart, which is what the tool offers to finish. Classes related to nothing are shown too rather than hidden. Aliases: show relationship, list relationships, relationships. A piped run gets the same answer as lines. | `show relationships` |

## Documentation

| Command | Description | Example |
| --- | --- | --- |
| `generate design document` | Generates a design document for the current project. | `generate design document` |
| `generate documentation` | Generates a design document for the current project. | `generate documentation` |
| `generate project report` | Generates a design document for the current project. | `generate project report` |

## Interfaces

| Command | Description | Example |
| --- | --- | --- |
| `create interface <interface name>` | Creates a new interface. | `create interface IRepository` |

## Learning

| Command | Description | Example |
| --- | --- | --- |
| `reset learning` | Clears the learned command memory and the phrasings kept for review. Aliases: clear learning, forget learning. | `reset learning` |
| `show learning` | Lists learned command signatures and their confidence, then the phrasings to review: each line that was not understood, paired with the next command that worked. Writes that list to .codegen/phrase-candidates.tsv. Aliases: list learning, view learning. | `show learning` |

## Methods

| Command | Description | Example |
| --- | --- | --- |
| `add method <method name> to <class name>` | Adds a method to a class. | `add method Save to Employee` |
| `remove method <method name> from <class name>` | Removes a method from a class. When the name is overloaded it lists them and removes none. | `remove method Save from Employee` |
| `rename method <old> to <new> in <class name>` | Renames a method, every overload of it. References elsewhere are not updated. | `rename method Save to Store in Employee` |
| `show methods <class name>` | Lists all methods in a class. | `show methods of Employee` |

## Projects

| Command | Description | Example |
| --- | --- | --- |
| `create blazor project` | Creates a new Blazor project. | `create blazor project EmployeeManagement` |
| `create class library` | Creates a new class library. | `create class library EmployeeManagement` |
| `create console project` | Creates a new console project. | `create console project EmployeeManagement` |
| `create project` | Creates a new project. | `create project EmployeeManagement` |
| `create webapi project` | Creates a new Web API project. | `create webapi project EmployeeManagement` |
| `create worker service` | Creates a new Worker Service project. | `create worker service BackgroundJobs` |
| `create wpf project` | Creates a new WPF project. | `create wpf project EmployeeManagement` |
| `current project` | Shows the currently selected project. | `current project` |
| `list projects` | Lists all available projects. | `list projects` |
| `show project` | Shows the currently selected project (same as 'current project'). | `show project` |
| `use project` | Switches the active project. | `use project EmployeeManagement` |

## Properties

| Command | Description | Example |
| --- | --- | --- |
| `add property` | Adds a property to a class. | `add property Name string to Employee` |
| `change type of <property> to <type> in <class name>` | Changes a property's type to any C# type (int, decimal?, List<string>). An initial value written for the old type is removed. Also: retype property <property> to <type> in <class name>. | `change type of Pages to int in Book` |
| `list properties <class name>` | Lists all properties in a class. | `list properties of Employee` |
| `remove property` | Removes a property from a class. | `remove property Name from Employee` |
| `rename property <old> to <new> in <class name>` | Renames a property on a class. Aliases: field for property; on/of for in. | `rename property Name to FullName in Employee` |
| `show properties <class name>` | Lists all properties in a class. | `show properties of Employee` |

## RecycleBin

| Command | Description | Example |
| --- | --- | --- |
| `delete class <class name>` | Moves a class to DeletedClasses. | `delete class Employee` |
| `restore class <class name>` | Restores a deleted class by name, to the folder it was deleted from. | `restore class Employee` |
| `restore last class` | Restores the class deleted most recently, without naming it. | `restore last class` |
| `show deleted classes` | Displays all deleted classes. | `show deleted classes` |

## Utility

| Command | Description | Example |
| --- | --- | --- |
| `batch` | Enter several commands, review what was detected, then run only the approved ones. | `batch` |
| `chat` | Opens a page in the browser where every command except 'build app', 'batch' and 'demo' can be run in a conversation: a command's whole output appears below what you typed, tinted green when it worked, red when it failed and amber when the words were not understood. The header says which solution is loaded and which project the next command will act on, and 'Load' points the whole session - console included - at another .sln, .slnx, .csproj or folder. The box completes from the command catalog as you type, and shares its history with the prompt. Closing the tab ends the session. Alias: ui. Piped runs and CG_NO_WEB=1 keep the console. | `chat` |
| `clear` | Clears the screen (history is kept). Alias: cls. | `clear` |
| `cls` | Alias for clear (clears the screen; history is kept). | `cls` |
| `demo` | Runs a short guided walkthrough: creates a small project, adds a class with two validation rules, generates the validator and shows the result. Asks before writing anything, and names the folder it creates so you can delete it afterwards. | `demo` |
| `exit` | Exits the application. | `exit` |
| `help` | Shows a one-screen overview: a few commands to start with, and every category with its command count. | `help` |
| `help <topic>` | Shows one category's commands. A topic is any category name 'help' lists, spelled however you like; a command name works too, so 'help approve' explains that command. | `help build app` |
| `help all` | Every command in one list — the complete catalog, which is what plain 'help' used to print. | `help all` |
| `history` | Lists the last 20 commands (not itself recorded). | `history` |
| `redo` | Puts back what undo reversed. The next command that changes something clears it. | `redo` |
| `run <file>` | Runs a script of commands (.cgs) saved with save session or written by hand: every line, or none if one fails. One undo reverses the whole run. | `run library.cgs` |
| `save session <file> [replace]` | Saves this session's commands that worked and changed something, as a script for run. Won't overwrite a file unless you add replace. | `save session library.cgs` |
| `status` | Reports the workspace, the projects found and which is selected, any loaded rules, the AI state, and the Wikipedia store and its updater service. Startup prints only what is wrong; this prints everything. | `status` |
| `undo` | Reverses the last command that changed something: its files, the validation rules and the current project. A whole batch or build app is one step. This session only, up to 50 steps; refuses, naming the file, if you have edited one it would restore. | `undo` |

## Validation

| Command | Description | Example |
| --- | --- | --- |
| `add business validation <rule> in <class name>` | Adds an entity-level business rule, spanning more than one property: comparisons (after, before, must not exceed), required-when, either/or, arithmetic (equals A times B, plus, minus, divided by) and sameness (same as, different from). | `add business validation EndDate must be after StartDate in Booking` |
| `add validation <rule> on <property> in <class name>` | Adds a field-level data rule to a class in plain English. Understands required (or 'should not be blank'), numeric bounds (between, at least, at most, greater/less than, positive, not negative, not zero, whole number), text bounds (length between, at most <n> characters, starts or ends with, contains, uppercase, digits only), named formats (email, URL, phone number, postcode, GUID, IP address, date), relative dates (in the past, in the future, at least <n> years ago, within the last <n> days), must match <regex>, and a list of allowed values. Rules are saved in the workspace (.codegen/validation-rules.json) and are there again in the next session. | `add validation must be between 18 and 65 on Age in Employee` |
| `generate validator` | Generates a validator for a class. | `generate validator for Employee` |
| `remove business validation <number> from <class name>` | Removes one business rule, by the number show business validation prints. | `remove business validation 1 from Booking` |
| `remove validation from <class name>.<property>` | Removes every data rule on a field. Also: remove validation on <property> in <class name>; or remove validation <number> from <class name>, with the number show validation prints. | `remove validation from Employee.Name` |
| `show business validation <class name>` | Show business rules for a class. | `show business validation Booking` |
| `show validation <class name>` | Show data validation rules for a class. | `show validation Employee` |

## Set for you

- `relationships` — offered after any command except creating a class
- `show relationships` — draws what is related to what, as boxes and arrows

A piped or scripted run is never asked. See "Relationships between classes" in the user manual for what a relationship is and what each answer writes.

