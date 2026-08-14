using System.Xml.Linq;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Utility class for updating .csproj files, specifically for removing <Compile> entries when classes are deleted.
/// </summary>
public static class CsProjUpdater
{
    public static void RemoveCompileEntry(string projectDirectory, string className)
    {
        var csproj = Directory.GetFiles(projectDirectory, "*.csproj")
            .FirstOrDefault();

        if (csproj == null)
            return;

        var document = XDocument.Load(csproj);

        var compileNodes = document
            .Descendants()
            .Where(x =>
                x.Name.LocalName == "Compile" &&
                x.Attribute("Include") != null &&
                Path.GetFileNameWithoutExtension(
                    x.Attribute("Include")!.Value)
                .Equals(className,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var node in compileNodes)
            node.Remove();

        if (compileNodes.Count > 0)
            document.Save(csproj);
    }

    /// <summary>
    /// Does nothing, deliberately. Restoring a class needs no project-file change.
    /// </summary>
    /// <remarks>
    /// This used to add <c>&lt;Compile Include="&lt;file&gt;"/&gt;</c>. Every project this tool
    /// creates is SDK-style, and the SDK globs <c>.cs</c> from the project directory by default, so
    /// the entry was a duplicate and the project stopped building with <c>NETSDK1022</c> — the same
    /// root cause that <c>ProjectFileUpdater</c> had on the <c>create class</c> path. Putting the
    /// file back on disk is the whole of a restore.
    /// <para>
    /// Kept as a no-op rather than deleted so <see cref="ClassRestoreService"/> still reads as
    /// declaring its intent, and so the reason survives next to the code that used to be wrong.
    /// <see cref="RemoveCompileEntry"/> is still real work: entries written by earlier versions are
    /// out there in users' projects, and clearing one is a repair.
    /// </para>
    /// </remarks>
    public static void AddCompileEntry(string projectDirectory, string filePath)
    {
    }
}