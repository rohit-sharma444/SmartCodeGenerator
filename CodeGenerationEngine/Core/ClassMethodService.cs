using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides services for adding methods to existing classes in a C# project.
/// </summary>
public static class ClassMethodService
{
    public static bool AddMethod(string workspaceRoot, string className,
        string methodName, string returnType, out string message)
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

            if (Regex.IsMatch(code, $@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase))
            {
                message = $"Method '{methodName}' already exists.";
                return false;
            }

            var methodCode = $@"public {returnType} {methodName}()
{{
{GenerateBody(returnType)}}}";

            if (!ClassMemberEditor.TryAddMember(file, className, methodCode, out var error))
            {
                message = error;
                return false;
            }

            message = $"Method '{methodName}' added to '{className}'.";

            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static string GenerateBody(string returnType)
    {
        return returnType switch
        {
            "void" => "            // TODO: implement\n",
            "string" => "            return string.Empty;\n",
            "bool" => "            return false;\n",
            "int" => "            return 0;\n",
            _ => $"            return default({returnType});\n"
        };
    }
}