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
            Command = "list class",
            Description = "Displays the class source code.",
            Example = "list class Employee"
        },
        new()
        {
            Category = "Class",
            Command = "list classes in <project name>",
            Description = "Displays the class source code.",
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
            Command = "create <class name> class inherit <base class>",
            Description = "Creates a new class that inherits from a base class.",
            Example = "create Employee class inherit Person"
        },
#endregion

#region"RecycleBin"
        new()
        {
            Category = "RecycleBin",
            Command = "delete class <class name>",
            Description = "Moves a class to DeletedClasses.",
            Example = "delete class <Employee?"
        },
        new()
        {
            Category = "RecycleBin",
            Command = "restore class <class name>",
            Description = "Restores a deleted class.",
            Example = "restore class <Employee?"
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
        new()
        {
            Category = "Methods",
            Command = "list methods <class name>",
            Description = "Lists all methods in a class.",
            Example = "list methods of Employee"
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
            Command = "add validation <validation> on <property> in <class name>",
            Description = "Adds validation to a class.",
            Example = "add validation Employee Id should not be blank on EmpID in Employee"
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
            Command = "show validation ",
            Description = "Show validation rules for a class.",
            Example = "show validation rule for <Employee>"
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
            Command = "generate project report",
            Description = "Generates a report for the current project.",
            Example = "generate project report"
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
                lines.Add($"{command.Command,-45} {command.Description}");

            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }
}