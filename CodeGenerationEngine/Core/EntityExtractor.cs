using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Extracts code/file entities (properties, namespace) from generated class
/// files on disk, and drives validator generation for a resolved class.
/// </summary>
public static class EntityExtractor
{
    public static bool GenerateValidator(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
        {
            Console.WriteLine("Class name is required.");
            return false;
        }

        var rules = ValidationRegistry.Get(command.ClassName);

        if (rules.Count == 0)
        {
            Console.WriteLine($"No validation rules found for {command.ClassName}.");
            return false;
        }

        var project = ProjectManager.CurrentProject;
        if (project == null)
        {
            Console.WriteLine("No project selected.");
            return false;
        }

        var classFile = Directory
            .EnumerateFiles(project.ProjectPath,
                $"{command.ClassName}.cs",
                SearchOption.AllDirectories).FirstOrDefault();

        if (classFile == null)
        {
            Console.WriteLine($"Class '{command.ClassName}' not found.");
            return false;
        }

        var properties = ExtractProperties(classFile);

        var namespaceName = ExtractNamespace(classFile);

        var validationCode = ValidationEngine.EmitValidationPartial(
                namespaceName, command.ClassName, properties, rules);

        var validatorFile = Path.Combine(Path.GetDirectoryName(classFile)!,
                $"{command.ClassName}.Validation.cs");

        File.WriteAllText(validatorFile, validationCode);

        Console.WriteLine($"Validator generated: {validatorFile}");
        return true;
    }

    private static string ExtractNamespace(string classFile)
    {
        var source = File.ReadAllText(classFile);

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var fileScoped = root.Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (fileScoped != null)
            return fileScoped.Name.ToString();

        var traditional = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return traditional?.Name.ToString() ?? "GeneratedProject";
    }


    public static List<PropertyDefinition> ExtractProperties(string classFile)
    {
        var code = File.ReadAllText(classFile);

        var matches = Regex.Matches(code,
            @"public\s+([\w<>?]+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}");

        var properties = new List<PropertyDefinition>();

        foreach (Match match in matches)
        {
            properties.Add(new PropertyDefinition
            {
                Type = match.Groups[1].Value,
                Name = match.Groups[2].Value
            });
        }

        return properties;
    }
}
