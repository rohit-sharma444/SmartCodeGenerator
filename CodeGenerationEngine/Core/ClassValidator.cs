using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to validate class definitions
/// </summary>
public static class ClassValidator
{
    public static List<string> Validate(ClassDefinition classDef)
    {
        var errors = new List<string>();

        var classResult = ClassNameValidator.Validate(classDef.Name);

        if (!classResult.IsValid)
            errors.Add(classResult.Message);

        foreach (var property in classDef.Properties)
        {
            var result = PropertyValidator.Validate(property);

            if (!result.IsValid)
                errors.Add(result.Message);

            result = TypeValidator.Validate(property.Type);

            if (!result.IsValid)
                errors.Add(result.Message);
        }

        var duplicateResult = DuplicatePropertyValidator.Validate(classDef.Properties);

        if (!duplicateResult.IsValid)
            errors.Add(duplicateResult.Message);

        foreach (var method in classDef.Methods)
        {
            var result = MethodValidator.Validate(method);

            if (!result.IsValid)
                errors.Add(result.Message);
        }

        return errors;
    }

    public static string ShowClass(string className)
    {
        var project = ProjectManager.CurrentProject;

        if (project == null)
        {
            Console.WriteLine("No project selected.");
            return "No project selected.";
        }

        var projectFolder = Path.GetDirectoryName(project.ProjectPath);

        if (string.IsNullOrWhiteSpace(projectFolder))
            return "Project folder not found.";

        // Rest of the method remains the same
        // ...

        var classFile = Directory
            .GetFiles(projectFolder, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(x =>
                Path.GetFileNameWithoutExtension(x)
                    .Equals(className, StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("\\bin\\") &&
                !x.Contains("\\obj\\") &&
                !x.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase));

        if (classFile == null)
        {
            Console.WriteLine($"Class '{className}' not found.");
            return "Class not found.";
        }

        Console.WriteLine();
        Console.WriteLine($"----- {className} -----");
        Console.WriteLine();
        Console.WriteLine(File.ReadAllText(classFile));
        Console.WriteLine();
        return "Class details displayed.";
    }

    public static bool IsValidClassName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "class", "classs", "classes", "with", "properties", "property", "create", "generate", "new", "project", "is", "are"
        };

        if (reserved.Contains(name))
            return false;
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return false;
        if (name.Length == 1 && char.IsLower(name[0]))
            return false;

        return true;
    }

}