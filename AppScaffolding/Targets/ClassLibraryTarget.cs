using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>The reusable core only: model classes plus JSON store and repositories, no runnable
/// front end.</summary>
public sealed class ClassLibraryTarget : ITargetScaffolder
{
    public string Key => "classlib";
    public string Description => "a class library (models + data only)";
    public DotNetProjectType ProjectType => DotNetProjectType.ClassLibrary;
    public string FolderSuffix => ".Lib";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => Array.Empty<GeneratedFile>();

    public void FixupProject(string outputFolder)
    {
        // `dotnet new classlib` drops a placeholder Class1.cs; the shared core replaces it.
        var class1 = Path.Combine(outputFolder, "Class1.cs");
        try
        {
            if (File.Exists(class1))
                File.Delete(class1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; a stray empty class compiles and is harmless.
        }
    }
}
