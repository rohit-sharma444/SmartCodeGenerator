using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeGenerationEngine.Helper;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Extracts code/file entities (properties, namespace) from generated class
/// files on disk, and drives validator generation for a resolved class.
/// </summary>
public static class EntityExtractor
{
    public static bool GenerateValidator(ParsedCommand command, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
        {
            Console.WriteLine("Class name is required.");
            return false;
        }

        // It wrote IShape.Validation.cs declaring a partial class IShape beside the partial
        // interface IShape - a pair that does not compile.
        if (InterfaceImplementers.ValidationRefusalFor(workspaceRoot, command.ClassName) is { } refusal)
        {
            Output.Error(refusal);
            return false;
        }

        var rules = ValidationRegistry.Get(command.ClassName);

        if (rules.Count == 0)
        {
            Console.WriteLine($"No validation rules found for {command.ClassName}.");
            return false;
        }

        // The validator is written next to the class file, so a selected project is
        // not required: search its path when one is selected, otherwise fall back to
        // the workspace root. This keeps `generate validator` working on a fresh
        // checkout, or when the current project was cleared for sitting outside the
        // open folder.
        var classFile = FindClassFile(workspaceRoot, command.ClassName);

        if (classFile == null)
        {
            Output.Warning($"Class '{command.ClassName}' not found.");
            return false;
        }

        // generate validator wrote over a hand-written Person.Validation.cs without a word; the
        // refresh after a member edit already left such a file alone.
        var existing = ValidatorPathFor(classFile, DeclaredName(classFile, command.ClassName));
        if (StaleValidators.IsHandWritten(existing))
        {
            Output.Error($"{existing} was not written by generate validator, so it was not replaced. "
                + "Rename or move it, then generate the validator again.");
            return false;
        }

        var validatorFile = WriteValidator(classFile, command.ClassName, rules);

        Output.Success($"Validator generated: {validatorFile}");
        return true;
    }

    /// <summary>
    /// The class file <c>generate validator</c> writes beside: under the selected project when there
    /// is one, otherwise under <paramref name="workspaceRoot"/>.
    /// </summary>
    internal static string? FindClassFile(string workspaceRoot, string className)
    {
        var searchRoot = ProjectManager.CurrentProject?.ProjectPath;
        if (string.IsNullOrWhiteSpace(searchRoot))
            searchRoot = workspaceRoot;

        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return null;

        return Directory
            .EnumerateFiles(searchRoot, $"{className}.cs", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>
    /// The class names <see cref="FindClassFile"/> could find, from the file names under the same
    /// root: for suggesting one when a name is not found.
    /// </summary>
    internal static IReadOnlyList<string> ClassNames(string workspaceRoot)
    {
        var searchRoot = ProjectManager.CurrentProject?.ProjectPath;
        if (string.IsNullOrWhiteSpace(searchRoot))
            searchRoot = workspaceRoot;

        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return [];

        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}bin{separator}") && !f.Contains($"{separator}obj{separator}"))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(n => !n.Contains('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string ValidatorPathFor(string classFile, string className) =>
        Path.Combine(Path.GetDirectoryName(classFile)!, $"{className}.Validation.cs");

    /// <summary>Emits <c>&lt;Class&gt;.Validation.cs</c> beside the class from its current
    /// properties and <paramref name="rules"/>; returns the path written.</summary>
    internal static string WriteValidator(string classFile, string className, IReadOnlyList<IValidationRule> rules)
    {
        // The name as declared, not as typed: "item" found Item.cs (the search is case-insensitive)
        // and wrote `partial class item` beside `class Item`, a second class that does not compile.
        className = DeclaredName(classFile, className);

        // The generated file declares `public partial class <Class>`. A class this tool created is
        // not partial, and that pair does not compile - so the declaring half is marked here rather
        // than emitting a file that cannot build beside it.
        Validation.ClassValidationWriter.EnsurePartial(classFile, className);

        var properties = ExtractProperties(classFile, className);

        var namespaceName = ExtractNamespace(classFile);

        var validationCode = ValidationEngine.EmitValidationPartial(
                namespaceName, className, properties, rules);

        var validatorFile = ValidatorPathFor(classFile, className);

        File.WriteAllText(validatorFile, validationCode);
        return validatorFile;
    }

    internal static string DeclaredName(string classFile, string className)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(classFile)).GetRoot();
        var declared = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Select(t => t.Identifier.Text)
            .ToList();

        return declared.FirstOrDefault(n => n.Equals(className, StringComparison.Ordinal))
            ?? declared.FirstOrDefault(n => n.Equals(className, StringComparison.OrdinalIgnoreCase))
            ?? className;
    }

    private static string ExtractNamespace(string classFile)
    {
        var source = File.ReadAllText(classFile);

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var fileScoped = root.Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (fileScoped != null)
            return fileScoped.Name.ToString();

        var traditional = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return traditional?.Name.ToString() ?? "GeneratedProject";
    }


    /// <summary>
    /// The public data a class or record declares: its auto-implemented properties, and the
    /// parameters of a positional record.
    /// </summary>
    /// <remarks>
    /// Parsed rather than pattern-matched. The regex this replaced looked for
    /// <c>public T X { get; set; }</c> and nothing else, so a positional record reported no
    /// properties at all — the chat page's tree drew four methods for a record whose five
    /// parameters were on screen beside it in the source.
    /// <para>
    /// Auto-implemented only. A property with a body, or an expression-bodied one, is computed
    /// from other members; listing it as data would have <c>generate validator</c> writing rules
    /// for something with nothing to set. Accessors themselves are not restricted to
    /// <c>get; set;</c>, so <c>{ get; init; }</c> and a get-only auto-property now count, which
    /// the regex missed for the same reason it missed records.
    /// </para>
    /// </remarks>
    public static List<PropertyDefinition> ExtractProperties(string classFile)
    {
        if (!File.Exists(classFile))
            return [];

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(classFile)).GetRoot();

        var properties = new List<PropertyDefinition>();

        // Every type in the file, nested ones included. A property belongs to exactly one type's
        // Members, so walking all of them lists each property once.
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            properties.AddRange(PropertiesDeclaredBy(type));

        return properties;
    }

    /// <summary>
    /// The public data the type called <paramref name="typeName"/> declares: in
    /// <paramref name="classFile"/>, and - when it is partial - in its other partial declarations
    /// in the same project.
    /// </summary>
    /// <remarks>
    /// Only that type's own members. The per-file reader above also lists the properties of every
    /// other type in the file: a nested <c>Customer.Address</c>'s Street, a sibling record
    /// struct's Amount. A rule accepted on one of those wrote <c>this.Street</c> into Customer's
    /// validator, which does not compile (CS1061). And a property in another partial file
    /// (<c>Customer.Notes.cs</c>) was not found at all, so it could get no rule.
    /// </remarks>
    public static List<PropertyDefinition> ExtractProperties(string classFile, string typeName)
    {
        if (!File.Exists(classFile))
            return [];

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(classFile)).GetRoot();
        var all = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();

        // The exact spelling first, else a different case of it: "customer" finds Customer.
        var declared = all.Where(t => t.Identifier.Text.Equals(typeName, StringComparison.Ordinal)).ToList();
        if (declared.Count == 0)
            declared = [.. all.Where(t => t.Identifier.Text.Equals(typeName, StringComparison.OrdinalIgnoreCase))];

        if (declared.Count == 0)
            return [];

        var properties = declared.SelectMany(PropertiesDeclaredBy).ToList();

        var first = declared[0];

        if (first.Modifiers.Any(m => m.ValueText == "partial"))
        {
            var name = first.Identifier.Text;
            var scope = ScopeOf(first);

            foreach (var other in ClassFiles.Under(ProjectFolderOf(classFile)))
            {
                if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(classFile), StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                try
                {
                    text = File.ReadAllText(other);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!text.Contains("partial", StringComparison.Ordinal) || !text.Contains(name, StringComparison.Ordinal))
                    continue;

                properties.AddRange(CSharpSyntaxTree.ParseText(text).GetRoot()
                    .DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Where(t => t.Kind() == first.Kind()
                        && t.Identifier.Text.Equals(name, StringComparison.Ordinal)
                        && t.Modifiers.Any(m => m.ValueText == "partial")
                        && ScopeOf(t) == scope)
                    .SelectMany(PropertiesDeclaredBy));
            }
        }

        return properties;
    }

    // The namespace and the enclosing types: a nested partial Order is not the top-level one.
    private static string ScopeOf(TypeDeclarationSyntax node) =>
        string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()))
        + "/" + string.Join(".", node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text));

    /// <summary>The folder of the nearest project file at or above <paramref name="file"/>, or the
    /// file's own folder when there is none: a partial class's parts are all in one project.</summary>
    private static string ProjectFolderOf(string file)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(file));

        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            try
            {
                if (Directory.EnumerateFiles(at, "*.csproj").Any())
                    return at;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                break;
            }
        }

        return folder ?? ".";
    }

    /// <summary>The public data <paramref name="type"/> itself declares, not its nested types'.</summary>
    private static IEnumerable<PropertyDefinition> PropertiesDeclaredBy(TypeDeclarationSyntax type)
    {
        var properties = new List<PropertyDefinition>();
        {
            if (type is RecordDeclarationSyntax { ParameterList: { } positional })
            {
                foreach (var parameter in positional.Parameters)
                {
                    if (parameter.Type is null)
                        continue;

                    properties.Add(new PropertyDefinition
                    {
                        Type = parameter.Type.ToString(),
                        Name = parameter.Identifier.Text
                    });
                }
            }

            foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
            {
                // An interface's member is public unless it says otherwise, and add property writes
                // it with no modifier at all.
                var isPublic = property.Modifiers.Any(m => m.ValueText == "public")
                    || (type is InterfaceDeclarationSyntax
                        && !property.Modifiers.Any(m => m.ValueText is "private" or "protected" or "internal"));
                if (!isPublic)
                    continue;

                // A static member belongs to the type, not to an instance of it. `AppCustomization`
                // has a `public static AppCustomization Default { get; }`, and the tree listed it
                // among the record's own fields as though it were one - which would also have
                // `generate validator` writing a rule against a singleton.
                if (property.Modifiers.Any(m => m.ValueText == "static"))
                    continue;

                // No accessor list at all: `public string Full => First + " " + Last;`
                if (property.AccessorList is not { } accessors)
                    continue;

                // An accessor that does something is a computed member wearing a property's shape.
                if (accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
                    continue;

                if (!accessors.Accessors.Any(a => a.Keyword.ValueText == "get"))
                    continue;

                properties.Add(new PropertyDefinition
                {
                    Type = property.Type.ToString(),
                    Name = property.Identifier.Text
                });
            }
        }

        return properties;
    }
}
