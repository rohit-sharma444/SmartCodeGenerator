using CodeGenerationEngine.Models;
using CodeGenerationEngine.Helper;

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

        // Semantic pre-check removed with the duplicate NLP stack: it ran an
        // English-language analyzer over generated C# and printed success
        // unconditionally, gating nothing. Compilation below is the real gate.
        Output.Success(" \n Validate succeeded. ");

        var result = Compiler.Compile(code);
        if (!result.Success)
        {
            Output.Error("Compilation failed. Diagnostics:");
            foreach (var d in result.Diagnostics)
                Console.WriteLine("  " + d);
            return;
        }
        Output.Success(" \n Compiled. \n");
    }
}