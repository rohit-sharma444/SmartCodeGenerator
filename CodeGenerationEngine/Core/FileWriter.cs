using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;

namespace CodeGenerationEngine.Core;

public static class FileWriter
{
    public static string Save(string directory, string? className, string code)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            var match = Regex.Match(code, @"class\s+(\w+)");

            className = match.Success
                ? match.Groups[1].Value
                : "GeneratedClass";
        }

        var filePath = Path.Combine(directory, $"{className}.cs");

        File.WriteAllText(filePath, code);

        Output.Success($"Saved generated class to: {filePath}");

        return filePath;
    }
}