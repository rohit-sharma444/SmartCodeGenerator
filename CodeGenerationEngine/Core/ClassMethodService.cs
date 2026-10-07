using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides services for adding methods to existing classes in a C# project.
/// </summary>
/// <remarks>
/// The name is checked against every member of every partial declaration of the class, as rename
/// method checks it: a method named like a property was written (CS0102), and one named like the
/// class (CS0542). The return type is bound in the class's project, as add property and retype bind
/// theirs, so a type that does not exist is refused rather than written.
/// </remarks>
public static class ClassMethodService
{
    public static bool AddMethod(string workspaceRoot, string className,
        string methodName, string returnType, out string message)
    {
        message = string.Empty;

        if (MemberNames.NotAnIdentifier(methodName, "method") is { } bad)
        {
            message = bad.Replace("A new method", "A method", StringComparison.Ordinal);
            return false;
        }

        if (string.IsNullOrWhiteSpace(returnType))
            returnType = "void";

        try
        {
            if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
                return false;

            var part = parts[0];
            className = part.Node.Identifier.Text;

            if (MemberNames.Taken(parts, methodName, []) is { } taken)
            {
                message = taken + $" Method '{methodName}' was not added.";
                return false;
            }

            if (returnType != "void")
            {
                var type = SyntaxFactory.ParseTypeName(returnType);
                if (type.ContainsDiagnostics || type.ToString() != returnType.Trim())
                {
                    message = $"'{returnType}' is not a C# type.";
                    return false;
                }

                if (!TypeResolution.Resolves(workspaceRoot, part.File, part.Node, type, out message))
                    return false;
            }

            var methodCode = $@"public {returnType} {methodName}()
{{
{GenerateBody(returnType)}}}";

            if (!ClassMemberEditor.TryAddMember(part.File, className, methodCode, out var error))
            {
                message = error;
                return false;
            }

            message = $"Method '{methodName}' added to '{className}'.";

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
