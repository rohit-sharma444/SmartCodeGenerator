using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static class AnalyzeProjectCommand
{
    public static string Execute(ParsedCommand command, string workspace)
    {
        var projectName = command.ProjectName;

        if (string.IsNullOrWhiteSpace(projectName))
            projectName = ProjectManager.CurrentProject?.ProjectName;

        if (string.IsNullOrWhiteSpace(projectName))
            return "No project selected.";

        var projectPath = Directory
            .GetDirectories(workspace)
            .FirstOrDefault(d =>
                Path.GetFileName(d)
                    .Equals(projectName,
                        StringComparison.OrdinalIgnoreCase));

        if (projectPath == null)
            return $"Project '{projectName}' not found.";

        var csFiles = Directory.GetFiles(
            projectPath,
            "*.cs",
            SearchOption.AllDirectories);

        var sb = new StringBuilder();

        sb.AppendLine($"Project: {projectName}");
        sb.AppendLine();

        sb.AppendLine($"Classes: {csFiles.Length}");

        int classCount = 0;
        int propertyCount = 0;
        int methodCount = 0;

        foreach (var file in csFiles)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            classCount += root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Count();
            propertyCount += root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Count();
            methodCount += root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Count();
        }

        sb.AppendLine($"Classes: {classCount}");
        sb.AppendLine($"Properties: {propertyCount}");
        sb.AppendLine($"Methods: {methodCount}");

        var folders = Directory
            .GetDirectories(projectPath, "*",
                SearchOption.AllDirectories)
            .Length;

        sb.AppendLine($"Folders: {folders}");

        return sb.ToString();
    }

    private static int CountProperties(string code)
    {
        return Regex.Matches(
            code,
            @"\{\s*get;\s*set;\s*\}")
            .Count;
    }

    private static int CountMethods(string code)
    {
        return Regex.Matches(
            code,
            @"(public|private|protected|internal)\s+.*?\(")
            .Count;
    }
}