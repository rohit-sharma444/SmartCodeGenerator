using System.Text;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

public static class AnalyzeClassCommand
{
    public static string Execute(
        ParsedCommand command,
        string workspace)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
            return "Please specify a class name.";

        var file = Directory
            .GetFiles(workspace, $"{command.ClassName}.cs",
                SearchOption.AllDirectories)
            .FirstOrDefault();

        if (file == null)
            return $"Class '{command.ClassName}' not found.";

        var code = File.ReadAllText(file);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var classNode = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (classNode == null)
            return "No class found in file.";

        return BuildReport(file, classNode, code);
    }

    private static string BuildReport(
        string file,
        ClassDeclarationSyntax classNode,
        string code)
    {
        var sb = new StringBuilder();

        var properties = classNode.Members
            .OfType<PropertyDeclarationSyntax>()
            .ToList();

        var methods = classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .ToList();

        var fields = classNode.Members
            .OfType<FieldDeclarationSyntax>()
            .ToList();

        var constructors = classNode.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        var interfaces = classNode.BaseList?
            .Types
            .Select(x => x.ToString())
            .ToList() ?? [];

        sb.AppendLine($"Class: {classNode.Identifier.Text}");
        sb.AppendLine($"File : {Path.GetFileName(file)}");
        sb.AppendLine();

        sb.AppendLine($"Properties   : {properties.Count}");
        sb.AppendLine($"Methods      : {methods.Count}");
        sb.AppendLine($"Fields       : {fields.Count}");
        sb.AppendLine($"Constructors : {constructors.Count}");

        if (interfaces.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Inheritance:");

            foreach (var item in interfaces)
                sb.AppendLine($" - {item}");
        }

        sb.AppendLine();
        sb.AppendLine($"Lines Of Code: {code.Split('\n').Length}");

        AnalyzeWarnings(
            sb,
            classNode,
            methods,
            properties,
            code);

        return sb.ToString();
    }

    private static void AnalyzeWarnings(
        StringBuilder sb,
        ClassDeclarationSyntax classNode,
        List<MethodDeclarationSyntax> methods,
        List<PropertyDeclarationSyntax> properties,
        string code)
    {
        sb.AppendLine();
        sb.AppendLine("Warnings:");

        bool found = false;

        if (methods.Count > 20)
        {
            found = true;
            sb.AppendLine(
                $" ⚠ High method count ({methods.Count})");
        }

        if (properties.Count > 30)
        {
            found = true;
            sb.AppendLine(
                $" ⚠ High property count ({properties.Count})");
        }

        var lines = code.Split('\n').Length;

        if (lines > 500)
        {
            found = true;
            sb.AppendLine(
                $" ⚠ Large class ({lines} lines)");
        }

        var publicMethods = methods
            .Where(m =>
                m.Modifiers.Any(x =>
                    x.Text == "public"))
            .ToList();

        var undocumentedMethods = publicMethods
            .Count(m =>
                !m.GetLeadingTrivia()
                    .ToString()
                    .Contains("///"));

        if (undocumentedMethods > 0)
        {
            found = true;

            sb.AppendLine(
                $" ⚠ {undocumentedMethods} public methods missing XML comments");
        }

        if (!found)
            sb.AppendLine(" None");
    }
}