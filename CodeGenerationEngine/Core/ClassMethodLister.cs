using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Utility class to list public methods of a specified class within a given workspace.
/// </summary>
public static class ClassMethodLister
{
    public static bool ListMethods(string workspaceRoot, string className)
    {
        var file = Directory
            .GetFiles(workspaceRoot, "*.cs",
                SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(className, StringComparison.OrdinalIgnoreCase));

        if (file == null)
        {
            Output.Warning($"Class '{className}' not found.");
            return false;
        }

        var code = File.ReadAllText(file);

        var matches = Regex.Matches(code, @"public\s+([\w<>]+)\s+(\w+)\s*\(");

        if (matches.Count == 0)
        {
            Console.WriteLine("No methods found.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Methods in {className}:");
        Console.WriteLine();

        foreach (Match match in matches)
        {
            Console.WriteLine($"{match.Groups[1].Value} {match.Groups[2].Value}()");
        }
        return true;
    }
}