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
    /// <remarks>
    /// The return type is fully qualified rather than pulled in with a <c>using</c>: the
    /// compile-testing helpers in <c>ValidatorTemplateTests</c> flatten several of these emitted
    /// files into one compilation unit, and C# requires every <c>using</c> directive to precede
    /// all type declarations in that unit -- a <c>using</c> surviving into the middle of a
    /// flattened, multi-file compilation would not compile. Qualifying the name instead keeps
    /// this file correct both stand-alone and flattened.
    /// </remarks>
    public static string ValidatorInterface(string projectName) => $$"""
namespace {{projectName}}.Validation;

/// <summary>Validates one entity, returning a message per broken rule.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this entity breaks. Empty means valid.</summary>
    System.Collections.Generic.IReadOnlyList<string> Validate(T item);
}
""";

    /// <summary>Source of <c>Validation/IEntityLookup.cs</c>.</summary>
    public static string EntityLookupInterface(string projectName) => $$"""
namespace {{projectName}}.Validation;

/// <summary>Existence checks a validator needs for foreign keys. Injected so a validator can be
/// unit-tested with a fake and no store on disk.</summary>
public interface IEntityLookup
{
    /// <summary>True when a record of this type with this Id exists.</summary>
    bool Exists<TEntity>(int id) where TEntity : class, new();
}
""";

    /// <summary>Source of <c>Validation/StoreEntityLookup.cs</c>.</summary>
    /// <remarks>
    /// <see cref="System.Reflection.BindingFlags"/> is fully qualified for the same reason as
    /// <see cref="ValidatorInterface"/>'s return type: no <c>using</c> here can survive past a
    /// flattened, multi-file test compilation. <c>using {{projectName}}.Data;</c> is safe because
    /// the test helper strips that exact line before concatenating.
    /// </remarks>
    public static string StoreEntityLookup(string projectName) => $$"""
using {{projectName}}.Data;

namespace {{projectName}}.Validation;

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
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase);
        return (int)(property?.GetValue(item) ?? 0);
    }
}
""";

    /// <summary>Source of <c>Validation/ValidatorFactory.cs</c> for <paramref name="entityNames"/>.</summary>
    /// <remarks>
    /// <c>NullValidator</c>'s members are fully qualified for the same reason as
    /// <see cref="ValidatorInterface"/>'s return type: no <c>using</c> beyond
    /// <c>using {{projectName}}.Models;</c> (which the test helper strips) can survive into a
    /// flattened, multi-file test compilation.
    /// </remarks>
    public static string Factory(string projectName, IReadOnlyList<string> entityNames)
    {
        var arms = string.Join("\n", entityNames.Select(name => $$"""
        if (typeof(T) == typeof({{name}}))
            return (IValidator<T>)(object)new {{name}}Validator();
"""));

        if (arms.Length == 0)
            arms = "        // This app declares no entities.";

        return $$"""
using {{projectName}}.Models;

namespace {{projectName}}.Validation;

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
    public System.Collections.Generic.IReadOnlyList<string> Validate(T item) =>
        System.Array.Empty<string>();
}
""";
    }
}
