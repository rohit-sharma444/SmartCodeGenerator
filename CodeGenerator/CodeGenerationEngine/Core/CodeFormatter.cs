namespace CodeGenerationEngine.Core;

public static class CodeFormatter
{
    public static string Format(string code)
    {
        var needsAnnotations =
            code.Contains("[Required]") ||
            code.Contains("[StringLength(") ||
            code.Contains("[MinLength(") ||
            code.Contains("[Range(");

        if (!code.Contains("namespace "))
        {
            var header = needsAnnotations
                ? "using System.ComponentModel.DataAnnotations;"
                    + Environment.NewLine + Environment.NewLine
                : string.Empty;

            return header + "namespace GeneratedCode" +
                   Environment.NewLine + "{" + Environment.NewLine +
                   code + Environment.NewLine + "}";
        }

        if (needsAnnotations &&
            !code.Contains("using System.ComponentModel.DataAnnotations;"))
        {
            return "using System.ComponentModel.DataAnnotations;" +
                   Environment.NewLine + Environment.NewLine + code;
        }

        return code;
    }
}