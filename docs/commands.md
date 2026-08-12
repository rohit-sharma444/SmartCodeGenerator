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
| `add <name>` | Review loop: adds an entity to the candidate model. The name keeps the shape you type, so a compound survives; several words become one class name. | `add cash register` |
| `approve [target] [sqlite]` | Review loop: generates the app using the type chosen at the start. Name a target to override it: wpf (default), winforms, console, webapi, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the default; the wpf and winforms targets are JSON-only). Alias: a. | `approve webapi sqlite` |
| `build app` | Starts the same guided flow, asking for the description first, then the application type, then whether you already have data. | `build app` |
| `build app: <description>` | Describes a system in plain English, then asks which kind of app to build and whether you have data, discovers a domain model, and opens a review loop before any code is written. | `build app: a library that lends books to members` |
| `build app: <description> [customization options]` | Customizes the generated application. Options go LAST on the line, after any data source: company "<name>" sets the company (used in the project metadata, the window title and a copyright header); theme light\|dark and accent #rrggbb colour the desktop front ends; icon <path> supplies a .ico or .png; title "<pattern>" overrides the window title, where {company} and {app} are substituted; namespace <prefix> puts the generated code under that namespace root (CodisLtd.Shop), leaving the project and assembly names alone. | `build app: a shop company "Codis Ltd" namespace CodisLtd theme dark` |
| `build app: <description> [novalidation] [logging] [noseed]` | Chooses which optional layers are generated. 'novalidation' omits the Validation folder and its per-model validators; 'logging' adds a logging layer (off by default); 'noseed' omits the sample seed rows (rows imported from your own data file are unaffected). Interactively these are asked by the 'Customize appearance and features?' step, which remembers your answers in .codegen/app-profile.json. | `build app: a shop novalidation logging noseed` |
| `build app: <description> from <format> <location>` | Builds the model from existing data with no prompts when piped; interactively you are still asked the application type. Formats: csv, txt, json, excel (sql is not in this build yet). Add 'normalize' to apply every normalization change the data needs. | `build app: order tracking from csv D:\data\orders.csv normalize` |
| `cancel` | Review loop: abandons the model without generating anything. Alias: c. | `cancel` |
| `field <entity> <name>[:<type>]` | Review loop: adds a field to an entity. Omit the type to have it inferred; explicit types are string, int, decimal, bool or DateTime. | `field Book Title:string` |
| `remove <name>` | Review loop: removes an entity from the candidate model. | `remove Reservation` |
| `rename <old> <new>` | Review loop: renames an entity in the candidate model. | `rename Member Borrower` |
| `unfield <entity> <name>` | Review loop: removes a field from an entity. | `unfield Book Title` |
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
| `demo` | Runs the demo workflow. | `demo` |
| `exit` | Exits the application. | `exit` |
| `help` | Displays all available commands. | `help` |
| `history` | Lists the last 20 commands (not itself recorded). | `history` |

## Validation

| Command | Description | Example |
| --- | --- | --- |
| `add business validation <rule> in <class name>` | Adds an entity-level business rule to a class. | `add business validation EndDate must be after StartDate in Booking` |
| `add validation <rule> on <property> in <class name>` | Adds a field-level data rule to a class. Rules: required (or 'should not be blank'), must be between <min> and <max>, length must be between <min> and <max>, must be greater/less than <n>, must be a valid email, must match <regex>, must be <value>, <value>, ... | `add validation must be between 18 and 65 on Age in Employee` |
| `generate validator` | Generates a validator for a class. | `generate validator for Employee` |
| `show business validation <class name>` | Show business rules for a class. | `show business validation Booking` |
| `show validation <class name>` | Show data validation rules for a class. | `show validation Employee` |

