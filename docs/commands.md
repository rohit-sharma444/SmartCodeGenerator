# Command Reference

<!-- Generated from CommandDictionary.GetMarkdownReference(). Do not edit by hand.
     Regenerate: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`. -->

## Analysis

| Command | Description | Example |
| --- | --- | --- |
| `analyze class <class name>` | Analyzes a specific class. | `analyze class Employee` |
| `analyze project` | Analyzes the current project. | `analyze project` |

## Build App

| Command | Description | Example |
| --- | --- | --- |
| `add <name>` | Review loop: adds an entity to the candidate model. | `add Reservation` |
| `approve [target] [sqlite]` | Review loop: generates the app. Targets: wpf (default), console, webapi, classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the default; WPF is JSON-only). Alias: a. | `approve webapi sqlite` |
| `build app: <description>` | Describes a system in plain English, discovers a domain model for it, then opens a review loop before any code is written. | `build app: a library that lends books to members` |
| `cancel` | Review loop: abandons the model without generating anything. Alias: c. | `cancel` |
| `field <entity> <name>[:<type>]` | Review loop: adds a field to an entity. Omit the type to have it inferred; explicit types are string, int, decimal, bool or DateTime. | `field Book Title:string` |
| `remove <name>` | Review loop: removes an entity from the candidate model. | `remove Reservation` |
| `rename <old> <new>` | Review loop: renames an entity in the candidate model. | `rename Member Borrower` |
| `unfield <entity> <name>` | Review loop: removes a field from an entity. | `unfield Book Title` |

## Class

| Command | Description | Example |
| --- | --- | --- |
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
| `add validation <rule> on <property> in <class name>` | Adds a field-level data rule to a class. Recognized rule: 'required'. | `add validation required on Name in Employee` |
| `generate validator` | Generates a validator for a class. | `generate validator for Employee` |
| `show business validation <class name>` | Show business rules for a class. | `show business validation Booking` |
| `show validation <class name>` | Show data validation rules for a class. | `show validation Employee` |

