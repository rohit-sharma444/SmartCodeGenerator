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
namespace {{rootNamespace}}.Validation;

/// <summary>Validates one entity against the whole picture: the record, the operation, the stored
/// version, other records, and the acting user.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this operation breaks. An outcome with no errors means valid.</summary>
    ValidationOutcome Validate(ValidationContext<T> context);
}
""";

    /// <summary>Source of <c>Validation/IEntityLookup.cs</c>.</summary>
    public static string EntityLookupInterface(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Existence and reference counts a validator needs. Injected so a validator can be
/// unit-tested with a fake and no store on disk.</summary>
public interface IEntityLookup
{
    /// <summary>True when a record of this type with this Id exists.</summary>
    bool Exists<TEntity>(int id) where TEntity : class, new();

    /// <summary>How many records of this type carry this foreign key value.</summary>
    int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new();
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

    /// <inheritdoc />
    public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new()
    {
        var property = typeof(TEntity).GetProperty(foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // A name that is not there counts nothing rather than throwing. The name came from
        // ForeignKeys at generation time; if a rename ever leaves the two disagreeing, a quiet
        // zero leaves the delete behaving as it did before this phase, where a reflection
        // exception would refuse every delete in the app. It also skips a full-table scan for a
        // property that was never going to match on this large a store.
        if (property is null)
            return 0;

        var count = 0;
        foreach (var item in StoreFactory.Create<TEntity>().GetAll())
            if (property.GetValue(item) is int value && value == id)
                count++;

        return count;
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
    public ValidationOutcome Validate(ValidationContext<T> context) => ValidationOutcome.Valid;
}
""";
    }

    /// <summary>Rule name recorded by DomainModelMapper for a required field.</summary>
    private const string RequiredRule = "required";

    /// <summary>Rule name recorded by DomainModelMapper for a length bound.</summary>
    private const string MaxLengthRule = "maxlength";

    private const string MinValueRule = "minvalue";
    private const string MaxValueRule = "maxvalue";
    private const string ScaleRule = "scale";
    private const string MinDateRule = "mindate";
    private const string DateOnlyRule = "dateonly";

    /// <summary>
    /// Every (property type, rule name) pair <see cref="Checks"/> knows how to emit. Inference is
    /// tested against this, so a rule kind recorded with no emitter fails a test rather than
    /// vanishing into a generated validator that reports success over a check that is not there.
    /// </summary>
    /// <remarks>
    /// Pairs rather than bare names, because <see cref="Checks"/> emits every rule inside a guard
    /// on the property's TYPE: a name alone cannot say whether a rule will produce anything. A
    /// list of the seven names would call a <c>scale</c> rule on an int emittable, and a
    /// <c>required</c> rule on an int emittable, and both emit nothing at all. The type strings
    /// are the five the generator emits, compared as <see cref="Checks"/> compares them — Ordinal,
    /// against <see cref="PropertyDefinition.Type"/>.
    /// <para>
    /// <c>required</c> is a string-only pair on purpose: <c>string.IsNullOrWhiteSpace</c> is the
    /// only meaningful emptiness test among the five types. A non-nullable <c>int</c> cannot be
    /// null and <c>0</c> is a legitimate value, so there is nothing for a required check to
    /// assert.
    /// </para>
    /// </remarks>
    public static IReadOnlyCollection<(string Type, string Rule)> EmittableRules { get; } =
    [
        ("string", RequiredRule), ("string", MaxLengthRule),
        ("int", MinValueRule), ("int", MaxValueRule),
        ("decimal", MinValueRule), ("decimal", MaxValueRule), ("decimal", ScaleRule),
        ("DateTime", MinDateRule), ("DateTime", DateOnlyRule)
    ];

    /// <summary>
    /// Whether <see cref="Checks"/> emits anything for a <paramref name="ruleName"/> rule sitting
    /// on a property of type <paramref name="propertyType"/>.
    /// </summary>
    public static bool CanEmit(string propertyType, string ruleName)
        => EmittableRules.Any(pair =>
            string.Equals(pair.Type, propertyType, StringComparison.Ordinal)
            && string.Equals(pair.Rule, ruleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="property"/> carries the required rule DomainModelMapper recorded.
    /// </summary>
    /// <remarks>
    /// The one predicate both this validator's generated check and
    /// <see cref="Screens.RequiredFieldsTemplate"/>'s emitted map call, so the asterisk and the
    /// validator read a single scan rather than two independent copies that merely happen to
    /// agree today.
    /// </remarks>
    internal static bool IsRequired(PropertyDefinition property)
        => property.Rules.Any(r => string.Equals(r.Name, RequiredRule, StringComparison.OrdinalIgnoreCase));

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
        string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets,
        bool referencesRequired)
    {
        var blocks = new List<string>();

        foreach (var property in definition.Properties)
            if (Checks(property) is { Count: > 0 } checks)
                blocks.Add(Block(checks));

        var body = blocks.Count == 0
            ? "        // This model declares no inferred rules."
            : string.Join("\n\n", blocks);

        // Reads context.Lookup rather than a constructor-injected field: a static stage method
        // has no instance to hold one, and the context is exactly what Repository<T> supplies it
        // through at the call site. One Block() PER target, never one shared block for the whole
        // stage: Block's try/catch is silent, so a single throwing lookup must not be able to
        // skip the checks for every OTHER foreign key on this entity.
        var entityBlocks = new List<string>();

        foreach (var target in foreignKeyTargets)
        {
            var checks = new List<string>();

            if (referencesRequired)
            {
                checks.Add($"if (item.{target}Id == 0)");
                checks.Add($"    errors.Add(new ValidationError(\"{target}Id\", \"{target} is required.\"));");
                checks.Add($"else if (!context.Lookup.Exists<{target}>(item.{target}Id))");
                checks.Add($"    errors.Add(new ValidationError(\"{target}Id\", \"{target}Id does not refer to a known {target}.\"));");
            }
            else
            {
                checks.Add($"if (item.{target}Id != 0 && !context.Lookup.Exists<{target}>(item.{target}Id))");
                checks.Add($"    errors.Add(new ValidationError(\"{target}Id\", \"{target}Id does not refer to a known {target}.\"));");
            }

            entityBlocks.Add(Block(checks));
        }

        var entityStage = entityBlocks.Count == 0
            ? string.Empty
            : $$"""


    /// <summary>What cannot be judged from the record in hand: whether the records it names
    /// exist. Runs only after Field has passed, so a lookup never runs over a record whose own
    /// fields were wrong.</summary>
    private static void Entity(ValidationContext<{{definition.Name}}> context, List<ValidationError> errors)
    {
        var item = context.Item;
        if (item is null)
            return;

{{string.Join("\n\n", entityBlocks)}}
    }
""";

        var stageList = entityBlocks.Count == 0
            ? "        Security,\n        Field,"
            : "        Security,\n        Field,\n        Entity,";

        return $$"""
using System;
using System.Collections.Generic;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Validation;

/// <summary>Inferred rules for {{definition.Name}}. REGENERATED — edit
/// {{definition.Name}}Validator.Custom.cs instead.</summary>
/// <remarks>
/// No field, no constructor: every stage is a static method, and the one thing a stage used to
/// reach through an injected field — the entity lookup — now arrives on the context each call
/// carries, so there is nothing left for an instance to hold.
/// </remarks>
public sealed partial class {{definition.Name}}Validator : IValidator<{{definition.Name}}>
{
    /// <summary>Security first, then the inferred field rules, then -- only when this model
    /// declares a foreign key -- Entity. Security exists because of the operation rather than
    /// the data, and Field always owns the null guard and the hand-written hook, whether or not
    /// this model has any inferred rule at all; Entity is conditional on there being a lookup to
    /// run.</summary>
    private static readonly ValidationPipeline.Stage<{{definition.Name}}>[] Stages =
    [
{{stageList}}
    ];

    /// <inheritdoc />
    public ValidationOutcome Validate(ValidationContext<{{definition.Name}}> context)
        => ValidationPipeline.Run(Stages, context);

    /// <summary>Refuses an operation this user may not perform, before any field is examined —
    /// so an unauthorised caller is never told which fields were wrong.</summary>
    private static void Security(ValidationContext<{{definition.Name}}> context, List<ValidationError> errors)
    {
        if (!Permissions.Allows(context.Op, "{{definition.Name}}", context.UserRole))
            errors.Add(new ValidationError(null, Permissions.Refusal(context.Op, "{{definition.Name}}")));
    }

    /// <summary>The inferred per-property rules and the hand-written hook — in that order, after
    /// Security has already let the caller through. A foreign key is no longer checked here; see
    /// Entity.</summary>
    private static void Field(ValidationContext<{{definition.Name}}> context, List<ValidationError> errors)
    {
        var item = context.Item;
        if (item is null)
        {
            errors.Add(new ValidationError(null, "{{definition.Name}} is null."));
            return;
        }

{{body}}

        ValidateCustom(item, errors);
    }
{{entityStage}}
    /// <summary>Hand-written rules, implemented in {{definition.Name}}Validator.Custom.cs.</summary>
    private static partial void ValidateCustom({{definition.Name}} item, List<ValidationError> errors);
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
    private static partial void ValidateCustom({{className}} item, List<ValidationError> errors)
    {
    }
}
""";

    /// <summary>The declaration an outdated stub has to be changed to, for
    /// <paramref name="className"/>.</summary>
    public static string CustomHookDeclaration(string className)
        => $"private static partial void ValidateCustom({className} item, List<ValidationError> errors)";

    /// <summary>
    /// Whether <paramref name="source"/> — an already-written <c>Validator.Custom.cs</c> — still
    /// declares its hand-written hook in the shape emitted before the validation pipeline.
    /// </summary>
    /// <remarks>
    /// The pipeline changed the hook from an instance <c>partial void ValidateCustom(T item,
    /// List&lt;string&gt; errors)</c> to <c>private static partial void ValidateCustom(T item,
    /// List&lt;ValidationError&gt; errors)</c>. The <c>.Custom.cs</c> half is written once and
    /// never overwritten, so a folder generated by an earlier version of this tool keeps the old
    /// declaration beside a freshly regenerated <c>.Generated.cs</c> and the pair cannot compile:
    /// CS0759 on the implementing declaration, CS8795 on the defining one. An UNTOUCHED empty stub
    /// breaks it too, which is why this asks about the SIGNATURE and never about the body.
    /// <para>
    /// Deliberately a report and NOT a rewrite. Changing the declaration in place would leave a
    /// hand-written body that calls <c>errors.Add("a sentence")</c> failing CS1503 instead — a file
    /// the tool half-migrated, whose remaining error no longer names the thing that moved. An
    /// honest sentence naming the file and the replacement line is the better outcome, and it is
    /// the user's own two lines to change.
    /// </para>
    /// <para>
    /// Conservative by construction: only a single-line declaration is recognised, which is the
    /// only shape any version of <see cref="CustomStub"/> has ever emitted, and anything
    /// unrecognised is reported as current. A stub falsely accused would send someone editing a
    /// file that was already right; a stub missed still produces the compiler's own message.
    /// </para>
    /// </remarks>
    public static bool HasOutdatedCustomHook(string source)
    {
        var declaration = Regex.Match(
            source, @"^[^\r\n]*\bvoid\s+ValidateCustom\s*\([^)\r\n]*\)", RegexOptions.Multiline);

        if (!declaration.Success)
            return false;

        return !Regex.IsMatch(declaration.Value, @"\bstatic\b")
            || !Regex.IsMatch(declaration.Value, @"List\s*<\s*ValidationError\s*>");
    }

    /// <summary>
    /// <see cref="HasOutdatedCustomHook(string)"/> for a file on disk. Best-effort in the same
    /// spirit as <see cref="ReconcileCustomStubNamespace"/>: a stub that cannot be read is
    /// reported as current, because a scaffold must not fail over a warning it could not compose.
    /// </summary>
    public static bool HasOutdatedCustomHookAt(string path)
    {
        try
        {
            return File.Exists(path) && HasOutdatedCustomHook(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The sentence that reports one such stub: the file, and the line to put in it.
    /// </summary>
    /// <remarks>
    /// It says "the shape emitted before the validation pipeline" rather than quoting the old
    /// declaration back, because <see cref="HasOutdatedCustomHook(string)"/> recognises anything
    /// that is not the CURRENT shape — quoting a specific previous one would be a claim it did
    /// not check.
    /// </remarks>
    public static string OutdatedCustomHookReport(string file, string className)
        => $"{file} declares ValidateCustom in the shape emitted before the validation pipeline; "
         + $"change it to '{CustomHookDeclaration(className)}' and have each of its rules add a "
         + "ValidationError rather than a string";

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
        var name = property.Name;

        if (property.Type == "string")
        {
            if (IsRequired(property))
            {
                checks.Add($"if (string.IsNullOrWhiteSpace(item.{name}))");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} is required.\"));");
            }

            var max = property.Rules
                .FirstOrDefault(r => string.Equals(r.Name, MaxLengthRule, StringComparison.OrdinalIgnoreCase))
                ?.MaxLength;

            if (max.HasValue)
            {
                checks.Add($"if (item.{name} is {{ Length: > {max.Value} }})");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} must be {max.Value} characters or fewer.\"));");
            }

            // Deliberately loose: these exist to match what [EmailAddress]/[Phone] already give the
            // console and WPF front-ends, not to be authoritative format validators.
            if (property.Attributes.Contains("EmailAddress"))
            {
                checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name}))");
                checks.Add("{");
                checks.Add($"    var at = item.{name}!.IndexOf('@');");
                checks.Add($"    if (at <= 0 || at == item.{name}!.Length - 1)");
                checks.Add($"        errors.Add(new ValidationError(\"{name}\", \"{name} is not a valid email address.\"));");
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
                checks.Add($"            errors.Add(new ValidationError(\"{name}\", \"{name} is not a valid phone number.\"));");
                checks.Add("            break;");
                checks.Add("        }");
                checks.Add("    }");
                checks.Add("}");
            }
        }

        if (property.Type is "int" or "decimal")
        {
            var min = Rule(property, MinValueRule)?.MinValue;

            if (min.HasValue)
            {
                checks.Add($"if (item.{name} < {Literal(min.Value, property.Type)})");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} must be {min.Value} or more.\"));");
            }

            var max = Rule(property, MaxValueRule)?.MaxValue;

            if (max.HasValue)
            {
                checks.Add($"if (item.{name} > {Literal(max.Value, property.Type)})");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} must be {max.Value} or less.\"));");
            }
        }

        if (property.Type == "decimal" && Rule(property, ScaleRule)?.Scale is { } places)
        {
            checks.Add($"if (decimal.Round(item.{name}, {places}) != item.{name})");
            // "0 decimal places or fewer" is what a decimal(18,0) column showed the user, and
            // there is no fewer than none. decimal(P,0) is a common declaration, so that
            // sentence would be the one such a column shows every time it refuses a value.
            checks.Add(places == 0
                ? $"    errors.Add(new ValidationError(\"{name}\", \"{name} must be a whole number.\"));"
                : $"    errors.Add(new ValidationError(\"{name}\", \"{name} must have {places} decimal places or fewer.\"));");
        }

        if (property.Type == "DateTime")
        {
            if (Rule(property, MinDateRule)?.MinDate is { } floor)
            {
                // The default(DateTime) guard is what makes an OPTIONAL date fillable. The
                // generated model types every date column as a non-nullable DateTime, so an
                // untouched field is indistinguishable from 0001-01-01 -- which is below this
                // floor, and would refuse every record the user saved while leaving the field
                // blank. A date the user actually typed and the column cannot store is still
                // refused, which is the rule's whole purpose; a genuine blank is left to the
                // database, which stores it as NULL.
                checks.Add($"if (item.{name} != default(DateTime) && item.{name} < DateTime.Parse(\"{floor}\", System.Globalization.CultureInfo.InvariantCulture))");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} must be on or after {floor}.\"));");
            }

            if (Rule(property, DateOnlyRule) is not null)
            {
                checks.Add($"if (item.{name}.TimeOfDay != TimeSpan.Zero)");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", \"{name} must be a date with no time of day.\"));");
            }
        }

        return checks;
    }

    private static PropertyRule? Rule(PropertyDefinition property, string ruleName)
        => property.Rules.FirstOrDefault(
            r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A decimal literal that compiles against the property's own type. Emitting a bare
    /// <c>255</c> against a decimal is fine, but <c>2.5</c> against one needs the <c>m</c>
    /// suffix or the generated file will not compile.
    /// </summary>
    private static string Literal(decimal value, string type)
        => type == "decimal"
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m"
            : ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
