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

        var formattedCode = CodeFormatter.Format(code);

        var filePath = FileWriter.Save(target.Directory,
            parsed?.ClassName, formattedCode);

        ProjectFileUpdater.Update(target.ProjectFile, filePath, formattedCode);
    }
}