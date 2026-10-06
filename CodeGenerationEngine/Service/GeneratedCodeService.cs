using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
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

        // Written to <workspace>\Generated with no project file: nothing builds it, and it said nothing.
        if (string.IsNullOrWhiteSpace(target.ProjectFile))
            Output.Warning($"No project is selected, so {Path.GetFileName(filePath)} is not part of any project and nothing will build it. "
                + "Select one with 'use project <name>' or make one with 'create console project <name>', then create it again.");
    }
}