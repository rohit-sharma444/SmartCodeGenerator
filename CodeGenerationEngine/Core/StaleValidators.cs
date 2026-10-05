using CodeGenerationEngine.Core.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Validators generated before a property was removed, renamed or retyped, which still read it as
/// it was.
/// </summary>
/// <remarks>
/// Two kinds, handled differently because only one can be rebuilt:
/// <list type="bullet">
/// <item><c>&lt;Class&gt;.Validation.cs</c>, written by <c>generate validator</c> from the rules in
/// <see cref="ValidationRegistry"/> and the class's properties. Both are current after the edit,
/// so the file is simply written again.</item>
/// <item><c>&lt;Class&gt;Validator.Generated.cs</c>, written by <c>build app</c> from the build's
/// class definitions: rules inferred from the data, business and workflow rules, foreign keys and
/// entity roles. None of that is kept with the app, so it cannot be rebuilt here, and rerunning
/// the build would overwrite the edit itself. Those files are named instead.</item>
/// </list>
/// </remarks>
public static class StaleValidators
{
    /// <summary>The emitter's own signature, so a hand-written Item.Validation.cs is never replaced.</summary>
    private const string EmittedSignature = "public System.Collections.Generic.List<string> Validate()";

    /// <summary>
    /// Writes <c>&lt;Class&gt;.Validation.cs</c> again when <c>generate validator</c> wrote one.
    /// Returns its path, or null when there was none to refresh.
    /// </summary>
    public static string? Regenerate(string workspaceRoot, string className)
    {
        var classFile = EntityExtractor.FindClassFile(workspaceRoot, className);
        if (classFile == null)
            return null;

        className = EntityExtractor.DeclaredName(classFile, className);
        var validatorFile = EntityExtractor.ValidatorPathFor(classFile, className);
        if (!File.Exists(validatorFile))
            return null;

        string existing;
        try
        {
            existing = File.ReadAllText(validatorFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!existing.Contains(EmittedSignature, StringComparison.Ordinal))
            return null;

        EntityExtractor.WriteValidator(classFile, className, ValidationRegistry.Get(className));
        return validatorFile;
    }

    /// <summary>
    /// The <c>&lt;Class&gt;Validator.Generated.cs</c> files under the workspace that read
    /// <paramref name="property"/> as a member, as paths relative to the workspace.
    /// </summary>
    public static IReadOnlyList<string> AppValidatorsReading(string workspaceRoot, string className, string property)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(property))
            return found;

        var fileName = $"{className}Validator.Generated.cs";
        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            if (!Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains(property, StringComparison.OrdinalIgnoreCase))
                continue;

            var reads = CSharpSyntaxTree.ParseText(text).GetRoot()
                .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(a => a.Name.Identifier.Text.Equals(property, StringComparison.OrdinalIgnoreCase));
            if (reads)
                found.Add(Path.GetRelativePath(workspaceRoot, file));
        }

        return found;
    }

    /// <summary>The warning for <see cref="AppValidatorsReading"/>, or null when none read it.</summary>
    /// <param name="change">What happened, e.g. "was removed", "was renamed to Name", "is now long".</param>
    public static string? AppValidatorWarning(string workspaceRoot, string className, string property, string change)
    {
        var files = AppValidatorsReading(workspaceRoot, className, property);
        if (files.Count == 0)
            return null;

        // As the app spells them, not as typed: the file is named for the class, and the
        // validator reads the property by its declared name.
        className = Path.GetFileName(files[0])[..^"Validator.Generated.cs".Length];
        try
        {
            var text = File.ReadAllText(Path.Combine(workspaceRoot, files[0]));
            property = CSharpSyntaxTree.ParseText(text).GetRoot()
                .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Select(a => a.Name.Identifier.Text)
                .FirstOrDefault(n => n.Equals(property, StringComparison.OrdinalIgnoreCase)) ?? property;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read a moment ago; the typed spelling will do.
        }

        return $"{string.Join(", ", files)} still checks {className}.{property} as it was, but {property} {change}. "
            + "build app generated it and does not regenerate it here (a rebuild would undo this edit), "
            + $"so update or delete the checks on {property} in it, or the app may not compile.";
    }
}
