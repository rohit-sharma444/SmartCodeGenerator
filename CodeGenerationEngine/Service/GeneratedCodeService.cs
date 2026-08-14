using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Service;

/// <summary>
/// This class is responsible for generating code based on the provided models and templates.
/// </summary>
public static class GeneratedCodeService
{
    public static void Save(ParsedCommand? parsed, string code,
        ProjectMetaData? currentProject, string workspaceRoot)
    {
        var target = ProjectLocator.Resolve(parsed, currentProject, workspaceRoot);

        // The project's own namespace, not a hardcoded one: a class written into Shop that declares
        // `GeneratedCode` is invisible to the rest of Shop without a using, and contradicts the
        // root namespace `build app:` sets. Falls back to GeneratedCode when there is no project.
        var formattedCode = CodeFormatter.Format(code, ProjectNamespace.For(target.ProjectFile));

        var filePath = FileWriter.Save(target.Directory,
            parsed?.ClassName, formattedCode);

        ProjectFileUpdater.Update(target.ProjectFile, filePath, formattedCode);
    }
}