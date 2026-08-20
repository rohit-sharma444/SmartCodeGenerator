# Command Reference

<!-- Generated from CommandDictionary.GetMarkdownReference(). Do not edit by hand.
     Regenerate: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`. -->

## AI

| Command | Description | Example |
| --- | --- | --- |
| `ai disable` | Turns AI recognition off. Command recognition returns to the rule-based path exactly as it behaves with AI absent. | `ai disable` |
| `ai enable` | Turns AI recognition on in ai.json. Off by default. Restart the CLI afterwards to load the model, then run 'ai reindex'. | `ai enable` |
| `ai mode <retrieval\|generative\|hybrid>` | Sets how AI reads unrecognized input. Only retrieval works today; generative and hybrid are declined until a later phase. | `ai mode retrieval` |
| `ai reindex` | Rebuilds the phrase index from the current commands. Run it after enabling AI, or whenever 'ai status' reports REINDEX REQUIRED. | `ai reindex` |
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
| `add <name>` | Review loop: adds a class to the candidate model. The name keeps the shape you type, so a compound survives; several words become one class name. | `add cash register` |
| `approve [target] [sqlite]` | Review loop: generates the app using the type chosen at the start. Name a target to override it: wpf (default), winforms, console, webapi, blazor, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the default; the wpf and winforms targets are JSON-only). Alias: a. | `approve webapi sqlite` |
| `build app` | Starts the same flow with nothing filled in. Interactively it opens a workflow page in the browser drawn as eight connected stages, ending in one that starts the application it just built. The stages are the navigation: each box shows the answer it holds, the box being answered carries the step forward and an arrow back one stage, the next box lights up when that step is ready, and clicking a finished box goes back to change it — there are no Back and Next buttons. Generate and Run stay disabled until the project has actually been written. A long step reports its progress as it works, and changing the description or the data source asks first, because it discards the model that was discovered from the old one; Cancel asks the same way once there is an answer to lose. The panel that opens holds the keyboard and Enter takes the step it offers — never Generate or Run, which stay deliberate clicks. Two tabs on the same workflow stay in step, and closing the last one ends the command. Piped runs keep the console wizard, as does CG_NO_WEB=1. | `build app` |
| `build app: <description>` | Describes a system in plain English. Interactively this opens a workflow page in the browser — the console prints the address and waits — where the rest of the build happens: application type, data, look, domain discovery, model review, generate-and-build, then running the finished application. Every step that takes a while says what it is doing and how long it has been doing it, and the compiler's output appears line by line as it builds. Piped runs are unchanged, and CG_NO_WEB=1 keeps the console wizard. | `build app: a library that lends books to members` |
| `build app: <description> [customization options]` | Customizes the generated application. Options go LAST on the line, after any data source: company "<name>" sets the company (used in the project metadata, the window title and a copyright header); theme light\|dark and accent #rrggbb colour the desktop front ends; icon <path> supplies a .ico or .png; title "<pattern>" overrides the window title, where {company} and {app} are substituted; namespace <prefix> puts the generated code under that namespace root (CodisLtd.Shop), leaving the project and assembly names alone; layout tabs\|sidebar\|master-detail\|dashboard chooses the shape of the window (wpf and winforms only). | `build app: a shop company "Codis Ltd" namespace CodisLtd theme dark layout sidebar` |
| `build app: <description> [novalidation] [logging] [noseed]` | Chooses which optional layers are generated. 'novalidation' omits the Validation folder and its per-model validators; 'logging' adds a logging layer (off by default); 'noseed' omits the sample seed rows (rows imported from your own data file are unaffected). Interactively these are asked by the 'Customize appearance and features?' step, which remembers your answers in .codegen/app-profile.json. | `build app: a shop novalidation logging noseed` |
| `build app: <description> from <format> <location>` | Builds the model from existing data with no prompts when piped; interactively the file is already answered and the workflow page opens on what is left. Formats: csv, txt, json, excel (sql is not in this build yet). Add 'normalize' to apply every normalization change the data needs. | `build app: order tracking from csv D:\data\orders.csv normalize` |
| `cancel` | Review loop: abandons the model without generating anything. Alias: c. | `cancel` |
| `field <class> <name>[:<type>]` | Review loop: adds a field to a class. Omit the type to have it inferred; explicit types are string, int, decimal, bool or DateTime. | `field Book Title:string` |
| `remove <name>` | Review loop: removes a class from the candidate model. | `remove Reservation` |
| `rename <old> <new>` | Review loop: renames a class in the candidate model. | `rename Member Borrower` |
| `unfield <class> <name>` | Review loop: removes a field from a class. | `unfield Book Title` |
| `why [name]` | Review loop: explains what discovery decided. Bare, it reports every decision grouped by outcome; with a name, it shows that name's whole trail — the stage, the reason and the evidence behind each step. | `why Cash register` |

## Class

| Command | Description | Example |
| --- | --- | --- |
| `<class name> inherits <base class name>` | Adds a base class to a class, keeping any existing interfaces. Aliases: extends, derives from. | `Order inherits AuditableBase` |
| `create class` | Creates a new class. | `create employee class` |
| `create class <class name> with properties` | Creates a new class with the specified properties. | `create employee class emdcode:int, Name:string` |
| `delete class` | Moves a class to DeletedClasses. | `delete class Employee` |
| `list classes` | Lists all classes in the current project. | `list classes` |
| `list classes in <project name>` | Lists all classes in a project. | `list classes in <EmployeeManagement>` |
| `list deleted classes` | Lists all deleted classes. | `list deleted classes` |
| `rename class` | Rename a class. | `rename class Employee to <newclass>` |
| `restore class` | Restores a deleted class. | `restore class Employee` |
| `show class` | Displays the class source code. | `show class Employee` |

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
| `reset learning` | Clears the learned command memory. Aliases: clear learning, forget learning. | `reset learning` |
| `show learning` | Lists learned command signatures and their confidence. Aliases: list learning, view learning. | `show learning` |

## Methods

| Command | Description | Example |
| --- | --- | --- |
| `add method <method name> to <class name>` | Adds a method to a class. | `add method Save to Employee` |
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
| `list properties <class name>` | Lists all properties in a class. | `list properties of Employee` |
| `remove property` | Removes a property from a class. | `remove property Name from Employee` |
| `rename property <old> to <new> in <class name>` | Renames a property on a class. Aliases: field for property; on/of for in. | `rename property Name to FullName in Employee` |
| `show properties <class name>` | Lists all properties in a class. | `show properties of Employee` |

## RecycleBin

| Command | Description | Example |
| --- | --- | --- |
| `delete class <class name>` | Moves a class to DeletedClasses. | `delete class <Employee>` |
| `restore class <class name>` | Restores a deleted class. | `restore class <Employee>` |
| `restore last class` | Restores a deleted class. | `restore last class` |
| `show deleted classes` | Displays all deleted classes. | `show deleted classes` |

## Utility

| Command | Description | Example |
| --- | --- | --- |
| `batch` | Enter several commands, review what was detected, then run only the approved ones. | `batch` |
| `clear` | Clears the screen (history is kept). Alias: cls. | `clear` |
| `cls` | Alias for clear (clears the screen; history is kept). | `cls` |
| `demo` | Runs a short guided walkthrough: creates a small project, adds a class with two validation rules, generates the validator and shows the result. Asks before writing anything, and names the folder it creates so you can delete it afterwards. | `demo` |
| `exit` | Exits the application. | `exit` |
| `help` | Shows a one-screen overview: a few commands to start with, and every category with its command count. | `help` |
| `help <topic>` | Shows one category's commands. A topic is any category name 'help' lists, spelled however you like; a command name works too, so 'help approve' explains that command. | `help build app` |
| `help all` | Every command in one list — the complete catalog, which is what plain 'help' used to print. | `help all` |
| `history` | Lists the last 20 commands (not itself recorded). | `history` |
| `status` | Reports the workspace, the projects found and which is selected, any loaded rules, the AI state, and the Wikipedia store and its updater service. Startup prints only what is wrong; this prints everything. | `status` |

## Validation

| Command | Description | Example |
| --- | --- | --- |
| `add business validation <rule> in <class name>` | Adds an entity-level business rule to a class. | `add business validation EndDate must be after StartDate in Booking` |
| `add validation <rule> on <property> in <class name>` | Adds a field-level data rule to a class. Rules: required (or 'should not be blank'), must be between <min> and <max>, length must be between <min> and <max>, must be greater/less than <n>, must be a valid email, must match <regex>, must be <value>, <value>, ... | `add validation must be between 18 and 65 on Age in Employee` |
| `generate validator` | Generates a validator for a class. | `generate validator for Employee` |
| `show business validation <class name>` | Show business rules for a class. | `show business validation Booking` |
| `show validation <class name>` | Show data validation rules for a class. | `show validation Employee` |

