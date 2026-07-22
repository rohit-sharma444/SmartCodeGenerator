# Command Reference

<!-- Generated from CommandDictionary.GetMarkdownReference(). Do not edit by hand.
     Regenerate: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`. -->

## Analysis

| Command | Description | Example |
| --- | --- | --- |
| `analyze class <class name>` | Analyzes a specific class. | `analyze class Employee` |
| `analyze project` | Analyzes the current project. | `analyze project` |
| `generate project report` | Generates a report for the current project. | `generate project report` |

## Class

| Command | Description | Example |
| --- | --- | --- |
| `create <class name> class inherit <base class>` | Creates a new class that inherits from a base class. | `create Employee class inherit Person` |
| `create class` | Creates a new class. | `create employee class` |
| `create class <class name> with properties` | Creates a new class with the specified properties. | `create employee class emdcode:int, Name:string` |
| `delete class` | Moves a class to DeletedClasses. | `delete class Employee` |
| `list class` | Displays the class source code. | `list class Employee` |
| `list classes in <project name>` | Lists all classes in a project. | `list classes in <EmployeeManagement>` |
| `list deleted classes` | Lists all deleted classes. | `list deleted classes` |
| `rename class` | Rename a class. | `rename class Employee to <newclass>` |
| `restore class` | Restores a deleted class. | `restore class Employee` |
| `show class` | Displays the class source code. | `show class Employee` |

## Interfaces

| Command | Description | Example |
| --- | --- | --- |
| `create interface <interface name>` | Creates a new interface. | `create interface IRepository` |

## Methods

| Command | Description | Example |
| --- | --- | --- |
| `add method <method name> to <class name>` | Adds a method to a class. | `add method Save to Employee` |
| `list methods <class name>` | Lists all methods in a class. | `list methods of Employee` |
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
| `add validation <validation> on <property> in <class name>` | Adds validation to a class. | `add validation Employee Id should not be blank on EmpID in Employee` |
| `generate validator` | Generates a validator for a class. | `generate validator for Employee` |
| `show business validation <class name>` | Show business rules for a class. | `show business validation Booking` |
| `show validation ` | Show validation rules for a class. | `show validation rule for <Employee>` |

