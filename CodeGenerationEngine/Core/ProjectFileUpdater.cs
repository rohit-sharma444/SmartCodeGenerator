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

            // No Compile item. Every project this tool creates is SDK-style, and the SDK globs
            // .cs from the project directory by default, so an explicit entry is a DUPLICATE and
            // the project stops building with NETSDK1022. Nothing needs adding: writing the file
            // into the project directory is already enough for it to be compiled. This is what
            // AppScaffolding has always done for `build app:`, and why those projects build.
            AddAnnotationPackage(doc, ns, code);
            doc.Save(csproj);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Project update failed: {ex.Message}");
        }
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