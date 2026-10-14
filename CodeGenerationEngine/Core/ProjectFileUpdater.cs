﻿using System.Xml.Linq;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Core;

/// <summary>
/// What a new class needs in its project file, added without touching the rest of it.
/// </summary>
/// <remarks>
/// The project file was loaded and saved on every <c>create class</c>, whether anything changed
/// or not, and the save rewrote it: an XML declaration and a byte-order mark added, tabs turned
/// into two spaces, blank lines and the final newline removed. It is now written only when a
/// line is added, and then as text, so everything else in it stays as the user had it.
/// </remarks>
public static class ProjectFileUpdater
{
    private const string AnnotationsPackage = "System.ComponentModel.Annotations";

    public static void Update(string? csproj, string generatedFile, string code)
    {
        if (string.IsNullOrWhiteSpace(csproj) || !File.Exists(csproj))
            return;

        try
        {
            // No Compile item. Every project this tool creates is SDK-style, and the SDK globs
            // .cs from the project directory by default, so an explicit entry is a DUPLICATE and
            // the project stops building with NETSDK1022. Nothing needs adding: writing the file
            // into the project directory is already enough for it to be compiled. This is what
            // AppScaffolding has always done for `build app:`, and why those projects build.
            var text = SourceFileText.Read(csproj, out var hasBom, out var newline);

            if (!NeedsAnnotationPackage(XDocument.Parse(text), code))
                return;

            var close = text.LastIndexOf("</Project>", StringComparison.Ordinal);
            if (close < 0)
                return;

            var unit = IndentUnit(text);
            var group = $"{unit}<ItemGroup>{newline}"
                + $"{unit}{unit}<PackageReference Include=\"{AnnotationsPackage}\" Version=\"5.0.0\" />{newline}"
                + $"{unit}</ItemGroup>{newline}";

            var lineStart = close == 0 ? 0 : text.LastIndexOf('\n', close - 1) + 1;
            var edited = string.IsNullOrWhiteSpace(text[lineStart..close])
                ? text[..lineStart] + group + text[lineStart..]
                : text[..close] + newline + group + text[close..];

            SourceFileText.Write(csproj, edited, hasBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Console.WriteLine(
                $"Project update failed: {ex.Message}");
        }
    }

    private static bool NeedsAnnotationPackage(XDocument doc, string code)
    {
        if (!code.Contains("[Required]"))
            return false;

        return !doc.Descendants()
            .Any(x => x.Name.LocalName == "PackageReference" &&
                x.Attribute("Include")?.Value == AnnotationsPackage);
    }

    /// <summary>The project file's own indentation: a tab when its lines start with one, else
    /// the spaces its first indented line has.</summary>
    private static string IndentUnit(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('\t'))
                return "\t";

            var spaces = line.Length - line.TrimStart(' ').Length;
            if (spaces > 0 && line.Trim().Length > 0)
                return new string(' ', spaces);
        }

        return "  ";
    }
}
