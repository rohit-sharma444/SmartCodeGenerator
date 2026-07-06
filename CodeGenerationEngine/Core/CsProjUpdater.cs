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

    public static void AddCompileEntry(string projectDirectory, string filePath)
    {
        var csproj = Directory.GetFiles(projectDirectory, "*.csproj").FirstOrDefault();

        if (csproj == null)
            return;

        var document = XDocument.Load(csproj);

        var relativePath = Path.GetRelativePath(projectDirectory, filePath);

        var alreadyExists = document
            .Descendants()
            .Any(x =>
                x.Name.LocalName == "Compile" &&
                string.Equals(
                    x.Attribute("Include")?.Value,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase));

        if (alreadyExists)
            return;

        var itemGroup = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "ItemGroup");

        if (itemGroup == null)
        {
            itemGroup = new XElement("ItemGroup");
            document.Root!.Add(itemGroup);
        }

        itemGroup.Add(new XElement("Compile", new XAttribute("Include", relativePath)));

        document.Save(csproj);
    }
}