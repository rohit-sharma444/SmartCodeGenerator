using System.Text.RegularExpressions;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides services for adding properties to existing classes.
/// Symmetric to ClassMethodService.
/// </summary>
public static class ClassPropertyService
{
    public static bool AddProperty(string workspaceRoot, string className,
        string propertyName, string propertyType, out string message)
    {
        message = string.Empty;

        try
        {
            var file = Directory.GetFiles(workspaceRoot, "*.cs",
                    SearchOption.AllDirectories).FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                message = $"Class '{className}' not found.";
                return false;
            }

            var code = File.ReadAllText(file);

            var pattern = $@"\b{Regex.Escape(propertyName)}\b\s*\{{\s*get\s*;\s*set\s*;\s*\}}";
            if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                message = $"Property '{propertyName}' already exists.";
                return false;
            }

            var propertyCode = $@"        public {propertyType} {propertyName} {{ get; set; }}";

            var lastBrace = code.LastIndexOf('}');

            if (lastBrace < 0)
            {
                message = "Invalid class file.";
                return false;
            }

            code = code.Insert(lastBrace, propertyCode + Environment.NewLine);

            File.WriteAllText(file, code);

            message = $"Property '{propertyName}' of type '{propertyType}' added to '{className}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}