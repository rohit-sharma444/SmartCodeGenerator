using Microsoft.CodeAnalysis;
using CodeGenerationEngine.Models;
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
        string methodName, string returnType, IReadOnlyList<ParameterDefinition> parameters, bool isAbstract, out string message)
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

            // An abstract method needs an abstract class, and nothing is written otherwise.
            if (isAbstract && !parts.Any(p => p.Node.Modifiers.Any(SyntaxKind.AbstractKeyword)))
            {
                message = $"'{className}' is not abstract. Method '{methodName}' was not added.";
                return false;
            }

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

            // Each parameter type is bound where the method goes, as the return type is: a type
            // that does not exist is refused by its parameter, and nothing is written.
            foreach (var parameter in parameters)
            {
                var type = SyntaxFactory.ParseTypeName(parameter.Type);
                if (type.ContainsDiagnostics)
                {
                    message = $"Parameter '{parameter.Name}': '{parameter.Type}' is not a C# type. "
                        + $"Method '{methodName}' was not added.";
                    return false;
                }

                if (!TypeResolution.Resolves(workspaceRoot, part.File, part.Node, type, out var reason))
                {
                    message = $"Parameter '{parameter.Name}': {reason} Method '{methodName}' was not added.";
                    return false;
                }
            }

            var parameterList = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));
            var isStatic = part.Node.Modifiers.Any(SyntaxKind.StaticKeyword);
            var methodCode = isAbstract
                ? $"public abstract {returnType} {methodName}({parameterList});"
                : $@"public {(isStatic ? "static " : string.Empty)}{returnType} {methodName}({parameterList})
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
