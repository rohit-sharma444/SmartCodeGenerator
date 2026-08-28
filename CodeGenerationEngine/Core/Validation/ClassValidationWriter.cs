using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>What the writer did, or why it did nothing.</summary>
/// <remarks>
/// A refusal carries its reason because the reason is what the person on the page reads. "Could
/// not add the rule" is the message this project has learned not to print.
/// </remarks>
public sealed record ValidationWriteResult(bool Ok, string Message)
{
    public static ValidationWriteResult Refused(string message) => new(false, message);

    public static ValidationWriteResult Written(string message) => new(true, message);
}

/// <summary>
/// Puts one validation check into a class's own source file.
/// </summary>
/// <remarks>
/// The check goes inside a <c>Validate()</c> the writer owns - created on the first rule, extended
/// on every one after. Each check is one <c>try</c> block preceded by the rule's own
/// <see cref="IValidationRule.Describe"/> as a comment, which is both what a reader sees and what
/// tells the writer a rule is already there.
/// <para>
/// The edit is located with Roslyn and applied as text. Rewriting the syntax tree and printing it
/// back would reformat code the user wrote; inserting at a span leaves every other byte where it
/// was, which is the only reason it is acceptable to edit a hand-written file at all.
/// </para>
/// </remarks>
public static class ClassValidationWriter
{
    private const string MethodName = "Validate";

    /// <summary>The rules already written into this class, as their own descriptions.</summary>
    public static IReadOnlyList<string> ExistingRules(string classFile, string className)
    {
        if (!File.Exists(classFile))
            return [];

        var source = SourceFileText.Read(classFile, out _, out _);

        var type = FindType(source, className);
        var method = type is null ? null : FindValidate(type);

        if (method?.Body is null)
            return [];

        return method.Body.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))
            .Select(t => t.ToString().TrimStart('/').Trim())
            .Where(text => text.Length > 0
                        && !text.StartsWith("business rule skipped", System.StringComparison.Ordinal))
            .ToList();
    }

    public static ValidationWriteResult Add(string classFile, string className, IValidationRule rule)
    {
        if (!File.Exists(classFile))
            return ValidationWriteResult.Refused($"There is no file at {classFile}.");

        var source = SourceFileText.Read(classFile, out var hasBom, out var newline);

        var type = FindType(source, className);
        if (type is null)
            return ValidationWriteResult.Refused(
                $"No class called '{className}' in {Path.GetFileName(classFile)}.");

        if (type.OpenBraceToken.IsKind(SyntaxKind.None))
            return ValidationWriteResult.Refused(
                $"'{className}' is declared without a body, so there is nowhere to put a check. "
                + "Give it a { } body and try again.");

        var properties = EntityExtractor.ExtractProperties(classFile);

        var missing = rule.Properties
            .Where(name => !properties.Any(p =>
                string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
            return ValidationWriteResult.Refused(
                $"'{className}' has no property called {string.Join(" or ", missing)}. "
                + $"It has: {string.Join(", ", properties.Select(p => p.Name))}.");

        if (ExistingRules(classFile, className)
                .Any(text => string.Equals(text, rule.Describe(), System.StringComparison.OrdinalIgnoreCase)))
            return ValidationWriteResult.Refused($"{className} already has that rule.");

        var indent = IndentOf(source, type);
        var block = CheckBlock(rule, properties, indent, newline);

        var method = FindValidate(type);

        string updated;

        if (method is null)
        {
            updated = source.Insert(
                type.CloseBraceToken.FullSpan.Start, NewMethod(block, indent, newline));
        }
        else
        {
            var unsuitable = Unsuitable(method, className);
            if (unsuitable is not null)
                return ValidationWriteResult.Refused(unsuitable);

            var exit = method.Body!.Statements.OfType<ReturnStatementSyntax>().Last();

            updated = source.Insert(exit.FullSpan.Start, newline + block);
        }

        var verdict = Verify(source, updated);
        if (verdict is not null)
            return ValidationWriteResult.Refused(verdict);

        SourceFileText.Write(classFile, updated, hasBom);

        return ValidationWriteResult.Written(
            $"{rule.Describe()} - added to {className} in {Path.GetFileName(classFile)}.");
    }

    /// <summary>
    /// Marks a class <c>partial</c> when it is not, so a sibling partial file can compile beside it.
    /// </summary>
    /// <remarks>
    /// <c>create class</c> writes <c>public class X</c> and <c>generate validator</c> writes
    /// <c>public partial class X</c> next to it. That pair is CS0260, and it shipped because every
    /// test of the generated half supplied a declaring half it had marked partial itself.
    /// </remarks>
    public static bool EnsurePartial(string classFile, string className)
    {
        if (!File.Exists(classFile))
            return false;

        var source = SourceFileText.Read(classFile, out var hasBom, out _);

        var type = FindType(source, className);
        if (type is null)
            return false;

        if (type.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            return false;

        var updated = source.Insert(type.Keyword.SpanStart, "partial ");

        SourceFileText.Write(classFile, updated, hasBom);

        return true;
    }

    private static TypeDeclarationSyntax? FindType(string source, string className) =>
        CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == className);

    private static MethodDeclarationSyntax? FindValidate(TypeDeclarationSyntax type) =>
        type.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == MethodName
                              && m.ParameterList.Parameters.Count == 0);

    /// <summary>How many spaces the type declaration itself is indented by.</summary>
    /// <remarks>
    /// Everything the writer emits is placed relative to this, so a file-scoped namespace (type at
    /// column 0) and a braced one (type at column 4) both come out looking like the code around
    /// them.
    /// </remarks>
    private static int IndentOf(string source, TypeDeclarationSyntax type)
    {
        var start = type.SpanStart;
        var lineStart = source.LastIndexOf('\n', System.Math.Max(0, start - 1)) + 1;

        var spaces = 0;
        for (var i = lineStart; i < start && source[i] == ' '; i++)
            spaces++;

        return spaces;
    }

    /// <summary>One rule as its comment, its try block and its catch.</summary>
    private static string CheckBlock(
        IValidationRule rule,
        IReadOnlyList<Models.PropertyDefinition> properties,
        int indent,
        string newline)
    {
        var emitted = new StringBuilder();
        rule.Emit(new ValidationEmitContext(emitted, properties));

        // ValidationEmitContext.Line writes a fixed twelve spaces, which is the body of a method
        // on a class at column 0 - one level short of a statement inside the try block this puts
        // it in, and short by the class's own indentation as well. Both are the same delta: the
        // statements want indent + 12, and they arrive at 12.
        var body = Reindent(emitted.ToString(), indent, newline);

        var pad = new string(' ', indent + 8);

        var sb = new StringBuilder();
        sb.Append(pad).Append("// ").Append(rule.Describe()).Append(newline);
        sb.Append(pad).Append("try").Append(newline);
        sb.Append(pad).Append('{').Append(newline);
        sb.Append(body);
        sb.Append(pad).Append('}').Append(newline);
        sb.Append(pad).Append("catch (System.Exception) { }").Append(newline);

        return sb.ToString();
    }

    /// <summary>
    /// Shifts every non-empty line of an emitted block right by <paramref name="extra"/> spaces,
    /// onto the file's own newline.
    /// </summary>
    /// <remarks>
    /// Only ever rightwards: a rule emits at a fixed twelve spaces and the shallowest place this
    /// writer puts one is deeper than that, so there is no case to un-indent and no need for the
    /// logic that would do it.
    /// </remarks>
    private static string Reindent(string block, int extra, string newline)
    {
        var pad = new string(' ', extra);
        var sb = new StringBuilder();

        foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
                continue;

            sb.Append(pad).Append(line).Append(newline);
        }

        return sb.ToString();
    }

    private static string NewMethod(string block, int indent, string newline)
    {
        var member = new string(' ', indent + 4);
        var body = new string(' ', indent + 8);

        var sb = new StringBuilder();
        sb.Append(newline);
        sb.Append(member).Append("/// <summary>Every validation rule on this class. Managed by SmartCodeGenerator.</summary>").Append(newline);
        sb.Append(member).Append("public System.Collections.Generic.List<string> ").Append(MethodName).Append("()").Append(newline);
        sb.Append(member).Append('{').Append(newline);
        sb.Append(body).Append("var errors = new System.Collections.Generic.List<string>();").Append(newline);
        sb.Append(newline);
        sb.Append(block);
        sb.Append(newline);
        sb.Append(body).Append("return errors;").Append(newline);
        sb.Append(member).Append('}').Append(newline);

        return sb.ToString();
    }

    /// <summary>
    /// Whether an existing <c>Validate()</c> is one this writer may extend, and why not.
    /// </summary>
    /// <remarks>
    /// The bar is deliberately high. A method that is not obviously the one this writer created is
    /// the user's, and a tool that splices statements into a method someone wrote is a tool people
    /// stop pointing at their code.
    /// </remarks>
    private static string? Unsuitable(MethodDeclarationSyntax method, string className)
    {
        if (method.Body is null)
            return $"{className} already has an expression-bodied Validate(). "
                 + "Give it a { } body, or rename it, and try again.";

        if (!method.ReturnType.ToString().Replace(" ", "").EndsWith("List<string>",
                System.StringComparison.Ordinal))
            return $"{className} already has a Validate() returning "
                 + $"{method.ReturnType}. This writer only extends one returning List<string>.";

        var declaresErrors = method.Body.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(s => s.Declaration.Variables)
            .Any(v => v.Identifier.ValueText == "errors");

        if (!declaresErrors)
            return $"{className}'s Validate() does not declare an 'errors' list, so it was "
                 + "written by hand. Rename it and try again.";

        if (!method.Body.Statements.OfType<ReturnStatementSyntax>().Any())
            return $"{className}'s Validate() has no return statement to insert before.";

        return null;
    }

    /// <summary>
    /// Whether the edit is safe to save, and why not.
    /// </summary>
    /// <remarks>
    /// Compared against the file as it was, not against nothing: a class referring to a type
    /// declared in another file has never compiled on its own, which is the normal case in a real
    /// project. Refusing there would make the feature useless. So the rule is that the writer may
    /// not make things worse - and a file that no longer parses is always worse.
    /// </remarks>
    private static string? Verify(string before, string after)
    {
        var parse = CSharpSyntaxTree.ParseText(after).GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

        if (parse is not null)
            return $"The edit would not parse ({parse.GetMessage()}), so nothing was written.";

        if (!Compiler.Compile(before).Success)
            return null; // it did not compile before this either; not ours to judge

        var result = Compiler.Compile(after);
        if (result.Success)
            return null;

        var first = result.Diagnostics
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

        return $"The check would not compile ({first?.GetMessage()}), so nothing was written.";
    }
}
