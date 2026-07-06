using System.Xml.Linq;

namespace CodeGenerationEngine.Core;

public static class ProjectFileUpdater
{
    public static void Update(string? csproj, string generatedFile, string code)
    {
        if (string.IsNullOrWhiteSpace(csproj) || !File.Exists(csproj))
            return;

        try
        {
            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            AddCompileReference(doc, ns, csproj, generatedFile);
            AddAnnotationPackage(doc, ns, code);
            doc.Save(csproj);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Project update failed: {ex.Message}");
        }
    }

    private static void AddCompileReference(XDocument doc, XNamespace ns,
        string csproj, string generatedFile)
    {
        var relative = Path.GetRelativePath(Path.GetDirectoryName(csproj)!,
                generatedFile).Replace('\\', '/');

        var exists = doc.Descendants().Any(x =>
                x.Name.LocalName == "Compile" &&
                x.Attribute("Include")?.Value == relative);

        if (exists)
            return;

        var group = doc.Root!
            .Elements()
            .FirstOrDefault(x =>
                x.Name.LocalName == "ItemGroup");

        if (group == null)
        {
            group = new XElement(ns + "ItemGroup");
            doc.Root.Add(group);
        }

        group.Add(new XElement(ns + "Compile", new XAttribute("Include", relative)));
    }

    private static void AddAnnotationPackage(XDocument doc, XNamespace ns, string code)
    {
        if (!code.Contains("[Required]"))
            return;

        var exists = doc.Descendants()
            .Any(x => x.Name.LocalName == "PackageReference" &&
                x.Attribute("Include")?.Value == "System.ComponentModel.Annotations");

        if (exists)
            return;

        var group = new XElement(ns + "ItemGroup");

        group.Add(
            new XElement(ns + "PackageReference",
                new XAttribute("Include", "System.ComponentModel.Annotations"),
                new XAttribute("Version", "5.0.0")));

        doc.Root!.Add(group);
    }
}