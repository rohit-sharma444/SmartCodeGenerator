using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>Generates a .NET project and one C# class file per domain entity.</summary>
public sealed class AppScaffolder
{
    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>.
    /// Best-effort: a file that cannot be written is skipped and the rest continue.
    /// Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes)
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            try
            {
                var source = RoslynClassGenerator.Generate(definition);
                var path = Path.Combine(outputFolder, definition.Name + ".cs");
                File.WriteAllText(path, source);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip this file; remaining classes are still written.
            }
        }

        return written;
    }
}
