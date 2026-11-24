using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Turns the default value a user typed for a property into a C# expression, and checks that the
/// expression fits the property's type by compiling it.
/// </summary>
/// <remarks>
/// Parsing is not enough: <c>abc</c> parses as an expression for an <c>int</c> and does not
/// compile. So the field <c>T x = expression;</c> goes into a compilation beside the project's own
/// files (as <see cref="TypeResolution"/> binds a type), with the project's nullable setting and
/// implicit usings, and any error on it is the refusal.
/// </remarks>
public static class DefaultValues
{
    private static readonly Regex PlainNumber = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// True when <paramref name="rawValue"/> is a value of <paramref name="type"/>, with the C#
    /// expression for it in <paramref name="expression"/>; otherwise <paramref name="error"/> says so.
    /// </summary>
    /// <param name="projectFolder">The folder of the project the property goes into, or null to
    /// check the value alone.</param>
    public static bool TryExpression(
        string type, string rawValue, string? projectFolder, out string expression, out string? error)
    {
        var value = (rawValue ?? string.Empty).Trim();
        expression = Expression(type, value);
        error = null;
        if (expression.Length > 0 && Compiles(type, expression, projectFolder, out var nullToNonNullable))
        {
            // It compiles, with warning CS8625: a property that is not nullable starts as null.
            if (!nullToNonNullable)
                return true;

            expression = string.Empty;
            error = $"'null' needs a nullable type: use {type.Trim()}?";
            return false;
        }

        // `default New` for an enum property is its member New.
        var member = $"{type.Trim().TrimEnd('?')}.{value}";
        if (Identifier.IsMatch(value) && Compiles(type, member, projectFolder, out _))
        {
            expression = member;
            return true;
        }

        expression = string.Empty;
        error = $"'{rawValue}' is not a valid {type} value.";
        return false;
    }

    private static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

    /// <summary>The refusal of a default on a struct property (the text create class and add property share).</summary>
    public static string StructRefusal(string structName)
        => $"A struct property gets its value in a constructor: add constructor to {structName}";

    /// <summary>The refusal of a default on an interface property.</summary>
    public const string InterfaceDefaultRefusal = "An interface property has no default value.";

    /// <summary>The refusal of private, static, required or readonly on an interface property.</summary>
    public const string InterfaceModifierRefusal = "An interface property takes no modifiers.";

    /// <summary>The refusal of a default that is not a value of the type, naming the property.</summary>
    public static string ValueRefusal(string error, string propertyName)
        => $"{error.TrimEnd('.')} for {propertyName}.";

    private static string Expression(string type, string value)
    {
        var bare = type.Trim().TrimEnd('?');
        if (bare is "string" or "String" or "System.String")
        {
            // The word null is the C# literal, not the text "null".
            if (value == "null" || (value.Length >= 2 && value[0] == '"' && value[^1] == '"'))
                return value;

            // 'New' is the text New in single quotes, as many people type a string.
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                value = value[1..^1];

            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        // True is how the word is often typed; C# spells it true.
        if (bare is "bool" or "Boolean" or "System.Boolean"
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase)))
            return value.ToLowerInvariant();

        // A one-letter char typed without its quotes: default A is 'A'.
        if (bare is "char" or "Char" or "System.Char" && value.Length == 1)
            return SymbolDisplay.FormatLiteral(value[0], quote: true);

        if (PlainNumber.IsMatch(value))
            return bare switch
            {
                "decimal" or "Decimal" => value + "m",
                "float" or "Single" => value + "f",
                _ => value,
            };

        return value;
    }

    /// <param name="nullToNonNullable">True when it compiles with CS8625: null given to a type that
    /// is not nullable, with nullable reference types on.</param>
    private static bool Compiles(string type, string expression, string? projectFolder, out bool nullToNonNullable)
    {
        nullToNonNullable = false;
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var usings = new List<string>();
        var trees = new List<SyntaxTree>();
        var nullable = NullableContextOptions.Enable;

        if (projectFolder is not null)
        {
            if (TypeResolution.ImplicitUsingsOn(projectFolder))
                usings.AddRange(TypeResolution.ImplicitUsings.Select(u => $"using {u};"));

            nullable = PropertyInitializers.ProjectNullable(projectFolder, Path.Combine(projectFolder, "Probe.cs"));
            foreach (var file in ClassFiles.Under(projectFolder))
            {
                try
                {
                    trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parse, path: file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable neighbour costs only the types it declares.
                }
            }

            // The probe has no namespace of its own, so it opens each one the project declares.
            usings.AddRange(trees
                .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                .Select(n => n.Name.ToString()).Distinct()
                .Select(n => $"using {n};"));
        }
        else
        {
            usings.AddRange(TypeResolution.ImplicitUsings.Select(u => $"using {u};"));
        }

        var source = string.Join(Environment.NewLine, usings.Distinct()) + Environment.NewLine
            + $"file class __DefaultProbe {{ {type} __Probe = {expression}; }}";
        var probe = CSharpSyntaxTree.ParseText(source, parse);

        // The expression must stay one initializer: no text that closes the field or the class.
        if (probe.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)
            || probe.GetRoot() is not CompilationUnitSyntax { Members: [ClassDeclarationSyntax { Members: [FieldDeclarationSyntax { Declaration.Variables.Count: 1 }] }] })
            return false;

        trees.Insert(0, probe);
        var compilation = CSharpCompilation.Create(
            "DefaultProbe", trees, Compiler.FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));

        var diagnostics = compilation.GetSemanticModel(probe).GetDiagnostics();
        nullToNonNullable = diagnostics.Any(d => d.Id == "CS8625");
        return !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
