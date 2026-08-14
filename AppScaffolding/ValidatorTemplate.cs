using System.Text.RegularExpressions;
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Emits the generated app's <c>Validation/</c> folder: the <c>IValidator&lt;T&gt;</c> seam, the
/// <c>IEntityLookup</c> foreign-key seam, a <c>ValidatorFactory</c> that resolves a validator from
/// a type, and one validator per model.
/// </summary>
/// <remarks>
/// The factory exists because the front-end templates are generic over T (EntityMenu&lt;T&gt;,
/// MapEntity&lt;T&gt;, EntityViewModel&lt;T&gt;) and so cannot name CustomerValidator directly. It
/// deliberately mirrors StoreFactory.Create&lt;T&gt;().
/// </remarks>
public static class ValidatorTemplate
{
    /// <summary>Source of <c>Validation/IValidator.cs</c>.</summary>
    public static string ValidatorInterface(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Validates one entity, returning a message per broken rule.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this entity breaks. Empty means valid.</summary>
    IReadOnlyList<string> Validate(T item);
}
""";

    /// <summary>Source of <c>Validation/IEntityLookup.cs</c>.</summary>
    public static string EntityLookupInterface(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Existence checks a validator needs for foreign keys. Injected so a validator can be
/// unit-tested with a fake and no store on disk.</summary>
public interface IEntityLookup
{
    /// <summary>True when a record of this type with this Id exists.</summary>
    bool Exists<TEntity>(int id) where TEntity : class, new();
}
""";

    /// <summary>Source of <c>Validation/StoreEntityLookup.cs</c>.</summary>
    public static string StoreEntityLookup(string rootNamespace) => $$"""
using System;
using System.Reflection;
using {{rootNamespace}}.Data;

namespace {{rootNamespace}}.Validation;

/// <summary>The real <see cref="IEntityLookup"/>: reads through the configured store.</summary>
public sealed class StoreEntityLookup : IEntityLookup
{
    /// <inheritdoc />
    public bool Exists<TEntity>(int id) where TEntity : class, new()
    {
        foreach (var item in StoreFactory.Create<TEntity>().GetAll())
            if (IdOf(item) == id)
                return true;

        return false;
    }

    private static int IdOf<TEntity>(TEntity item)
    {
        var property = typeof(TEntity).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(property?.GetValue(item) ?? 0);
    }
}
""";

    /// <summary>Source of <c>Validation/ValidatorFactory.cs</c> for <paramref name="entityNames"/>.</summary>
    public static string Factory(string rootNamespace, IReadOnlyList<string> entityNames)
    {
        var arms = string.Join("\n", entityNames.Select(name => $$"""
        if (typeof(T) == typeof({{name}}))
            return (IValidator<T>)(object)new {{name}}Validator();
"""));

        if (arms.Length == 0)
            arms = "        // This app declares no entities.";

        return $$"""
using System;
using System.Collections.Generic;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Validation;

/// <summary>Resolves the validator for an entity type. Mirrors StoreFactory.Create&lt;T&gt;().</summary>
public static class ValidatorFactory
{
    /// <summary>The validator for entity type <typeparamref name="T"/>, or a no-op validator when
    /// this app generated none for it.</summary>
    public static IValidator<T> Create<T>() where T : class, new()
    {
{{arms}}

        return new NullValidator<T>();
    }
}

/// <summary>Validates nothing. Repository&lt;T&gt; is generic, so an unmapped T must degrade to
/// "no rules" rather than throw.</summary>
internal sealed class NullValidator<T> : IValidator<T>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(T item) => Array.Empty<string>();
}
""";
    }

    /// <summary>Rule name recorded by DomainModelMapper for a required field.</summary>
    private const string RequiredRule = "required";

    /// <summary>Rule name recorded by DomainModelMapper for a length bound.</summary>
    private const string MaxLengthRule = "maxlength";

    /// <summary>
    /// Source of <c>Validation/&lt;Model&gt;Validator.Generated.cs</c>: the inferred rules, plus the
    /// call to the hand-written hook. Regenerated on every build, so nothing here may be edited.
    /// </summary>
    /// <remarks>
    /// Rules are read from the Rules/Attributes that DomainModelMapper.AddValidation already
    /// recorded, never re-inferred, so a model's attributes and its validator cannot disagree.
    /// Each property's checks sit in their own try/catch: it scopes any locals the checks declare,
    /// and it keeps one throwing rule from aborting the rest — the same best-effort stance
    /// ValidationEngine.EmitSection takes.
    /// </remarks>
    public static string Generated(
        string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets)
    {
        var blocks = new List<string>();

        foreach (var property in definition.Properties)
            if (Checks(property) is { Count: > 0 } checks)
                blocks.Add(Block(checks));

        foreach (var target in foreignKeyTargets)
            blocks.Add(Block(new List<string>
            {
                $"if (item.{target}Id != 0 && !_lookup.Exists<{target}>(item.{target}Id))",
                $"    errors.Add(\"{target}Id does not refer to a known {target}.\");",
            }));

        var body = blocks.Count == 0
            ? "        // This model declares no inferred rules."
            : string.Join("\n\n", blocks);

        return $$"""
using System;
using System.Collections.Generic;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Validation;

/// <summary>Inferred rules for {{definition.Name}}. REGENERATED — edit
/// {{definition.Name}}Validator.Custom.cs instead.</summary>
public sealed partial class {{definition.Name}}Validator : IValidator<{{definition.Name}}>
{
    private readonly IEntityLookup _lookup;

    /// <summary>Validates against the real store.</summary>
    public {{definition.Name}}Validator() : this(new StoreEntityLookup()) { }

    /// <summary>Validates against the supplied lookup. Tests pass a fake.</summary>
    public {{definition.Name}}Validator(IEntityLookup lookup) => _lookup = lookup;

    /// <inheritdoc />
    public IReadOnlyList<string> Validate({{definition.Name}} item)
    {
        var errors = new List<string>();
        if (item is null)
        {
            errors.Add("{{definition.Name}} is null.");
            return errors;
        }

{{body}}

        ValidateCustom(item, errors);
        return errors;
    }

    /// <summary>Hand-written rules, implemented in {{definition.Name}}Validator.Custom.cs.</summary>
    partial void ValidateCustom({{definition.Name}} item, List<string> errors);
}
""";
    }

    /// <summary>Source of the write-once <c>Validation/&lt;Model&gt;Validator.Custom.cs</c>.</summary>
    public static string CustomStub(string rootNamespace, string className) => $$"""
using System.Collections.Generic;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Validation;

/// <summary>Hand-written rules for {{className}}.</summary>
public sealed partial class {{className}}Validator
{
    /// <summary>Add rules the generator cannot infer — cross-field checks, business rules.
    /// Regenerating the app never overwrites this file.</summary>
    partial void ValidateCustom({{className}} item, List<string> errors)
    {
    }
}
""";

    /// <summary>
    /// Repoints an already-written <c>Validator.Custom.cs</c> stub's <c>namespace</c> declaration
    /// and any <c>using</c> line rooted at the app's own (old) namespace, at
    /// <paramref name="rootNamespace"/>. Best-effort and idempotent, in the same spirit as
    /// <see cref="WpfProjectFixup.ReconcileNamespace"/>.
    /// </summary>
    /// <remarks>
    /// This is the fourth reconciliation point: <see cref="CustomStub"/> is written once and never
    /// overwritten (the user's own hand-written rules live in its body, below the header), so a
    /// re-scaffold under a changed prefix would otherwise leave it declaring — and importing from —
    /// a namespace no generated file declares any more. Only the header moves; the body is never
    /// touched.
    /// <para>
    /// Matches the SHAPE, never a remembered previous value: the file's own current
    /// <c>namespace …Validation;</c> (or <c>namespace …Validation { … }</c>) line names the old
    /// root, and only <c>using</c> lines rooted there are rewritten. A user's unrelated addition —
    /// <c>using System.Text.RegularExpressions;</c>, or a reference into their own library — never
    /// starts with the old root and so is left alone.
    /// </para>
    /// <para>
    /// Both namespace forms: a file-scoped declaration is what every template here emits, but a
    /// user may have reformatted their stub to the block form since it was written, and this must
    /// not silently no-op on it — see <see cref="WpfProjectFixup.ReconcileNamespace"/>, which
    /// handles the same two shapes for the same reason. The namespace regexes below replace only
    /// <c>namespace X.Validation</c>, never the terminator that follows it, so whichever of
    /// <c>;</c> or <c>{</c> the file already has is left exactly as it was.
    /// </para>
    /// </remarks>
    public static void ReconcileCustomStubNamespace(string path, string rootNamespace)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var original = File.ReadAllText(path);

            var declaration = Regex.Match(
                original, @"^namespace\s+([^;{\s]+)\.Validation\b", RegexOptions.Multiline);
            if (!declaration.Success)
                return;

            var oldRoot = declaration.Groups[1].Value;

            var updated = Regex.Replace(
                original,
                $@"^using\s+{Regex.Escape(oldRoot)}\.",
                $"using {rootNamespace}.",
                RegexOptions.Multiline);

            updated = Regex.Replace(
                updated,
                $@"^namespace\s+{Regex.Escape(oldRoot)}\.Validation\b",
                $"namespace {rootNamespace}.Validation",
                RegexOptions.Multiline);

            if (!string.Equals(original, updated, StringComparison.Ordinal))
                File.WriteAllText(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash in the scaffolder.
        }
    }

    /// <summary>Wraps one property's (or one relationship's) checks in its own try/catch.</summary>
    private static string Block(IReadOnlyList<string> checks)
    {
        var indented = string.Join("\n", checks.Select(line => "            " + line));
        return "        try\n        {\n" + indented + "\n        }\n        catch (Exception) { }";
    }

    /// <summary>The checks for one property, from the rules DomainModelMapper recorded. A
    /// navigation property and a [Key] Id both yield none.</summary>
    private static List<string> Checks(PropertyDefinition property)
    {
        var checks = new List<string>();
        if (property.Type != "string")
            return checks;

        var name = property.Name;

        if (property.Rules.Any(r => string.Equals(r.Name, RequiredRule, StringComparison.OrdinalIgnoreCase)))
        {
            checks.Add($"if (string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add($"    errors.Add(\"{name} is required.\");");
        }

        var max = property.Rules
            .FirstOrDefault(r => string.Equals(r.Name, MaxLengthRule, StringComparison.OrdinalIgnoreCase))
            ?.MaxLength;

        if (max.HasValue)
        {
            checks.Add($"if (item.{name} is {{ Length: > {max.Value} }})");
            checks.Add($"    errors.Add(\"{name} must be {max.Value} characters or fewer.\");");
        }

        // Deliberately loose: these exist to match what [EmailAddress]/[Phone] already give the
        // console and WPF front-ends, not to be authoritative format validators.
        if (property.Attributes.Contains("EmailAddress"))
        {
            checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add("{");
            checks.Add($"    var at = item.{name}!.IndexOf('@');");
            checks.Add($"    if (at <= 0 || at == item.{name}!.Length - 1)");
            checks.Add($"        errors.Add(\"{name} is not a valid email address.\");");
            checks.Add("}");
        }

        if (property.Attributes.Contains("Phone"))
        {
            checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add("{");
            checks.Add($"    foreach (var character in item.{name}!)");
            checks.Add("    {");
            checks.Add("        if (!char.IsDigit(character) && character != ' ' && character != '+'");
            checks.Add("            && character != '-' && character != '(' && character != ')')");
            checks.Add("        {");
            checks.Add($"            errors.Add(\"{name} is not a valid phone number.\");");
            checks.Add("            break;");
            checks.Add("        }");
            checks.Add("    }");
            checks.Add("}");
        }

        return checks;
    }
}
