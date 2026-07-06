using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class SemanticCompilerRunner
{
    public static void RunSemanticAndCompile(ParsedCommand? command, string code, System.Collections.Generic.IEnumerable<PropertyRule> configuredRules)
    {
        if (code.Contains("[Required]") || code.Contains("[StringLength(") ||
            code.Contains("[MinLength(") || code.Contains("[Range("))
        {
            code = "using System.ComponentModel.DataAnnotations;" +
                Environment.NewLine + Environment.NewLine + code;
        }

        var semantic = SemanticAnalyzer.Analyze(code);
        var (isValid, validationErrors) = SemanticValidator.Validate(semantic);

        if (!isValid)
        {
            Console.WriteLine("Errors:");
            foreach (var err in validationErrors)
                Console.WriteLine("  " + err);
        }
        Console.WriteLine(" \n Validate succeeded. ");

        var result = Compiler.Compile(code);
        if (!result.Success)
        {
            Console.WriteLine("Compilation failed. Diagnostics:");
            foreach (var d in result.Diagnostics)
                Console.WriteLine("  " + d);
            return;
        }
        Console.WriteLine(" \n Compiled. \n");
    }
}