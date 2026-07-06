using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace CodeGenerationEngine.Core;

public static class DesignDocumentGenerator
{
    public static string Generate(ParsedCommand command, string workspace)
    {
        var projectName = ProjectManager.CurrentProject?.ProjectName;

        if (string.IsNullOrWhiteSpace(projectName))
            return "No project selected.";

        var projectPath = Directory
            .GetDirectories(workspace)
            .FirstOrDefault(x =>
                Path.GetFileName(x)
                    .Equals(projectName,
                        StringComparison.OrdinalIgnoreCase));

        if (projectPath == null)
            return $"Project '{projectName}' not found.";

        var document =
            BuildDocument(projectName, projectPath);

        var outputFile = Path.Combine(
            projectPath,
            "DesignDocument.md");

        File.WriteAllText(outputFile, document);

        return $"Design document generated:\n{outputFile}";
    }

    private static string BuildDocument(
        string projectName,
        string projectPath)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {projectName}");
        sb.AppendLine();
        sb.AppendLine("## Project Overview");
        sb.AppendLine();

        var files = Directory.GetFiles(
            projectPath,
            "*.cs",
            SearchOption.AllDirectories);

        sb.AppendLine($"Classes: {files.Length}");
        sb.AppendLine();

        sb.AppendLine("## Architecture");
        sb.AppendLine();

        foreach (var file in files)
        {
            AnalyzeFile(file, sb);
        }

        return sb.ToString();
    }

    private static void AnalyzeFile(
        string file,
        StringBuilder sb)
    {
        var code = File.ReadAllText(file);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        foreach (var cls in root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>())
        {
            sb.AppendLine($"### {cls.Identifier}");

            var baseTypes =
                cls.BaseList?.Types
                    .Select(x => x.ToString())
                    .ToList() ?? [];

            if (baseTypes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Implements / Inherits:");

                foreach (var item in baseTypes)
                    sb.AppendLine($"- {item}");
            }

            var properties = cls.Members
                .OfType<PropertyDeclarationSyntax>()
                .ToList();

            if (properties.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Properties:");

                foreach (var property in properties)
                {
                    sb.AppendLine(
                        $"- {property.Type} {property.Identifier}");
                }
            }

            var methods = cls.Members
                .OfType<MethodDeclarationSyntax>()
                .ToList();

            if (methods.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Methods:");

                foreach (var method in methods)
                {
                    sb.AppendLine(
                        $"- {method.Identifier}()");
                }
            }

            sb.AppendLine();
        }
    }
}