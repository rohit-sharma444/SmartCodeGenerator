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
    public static string ValidatorInterface(string projectName) => $$"""
using System.Collections.Generic;

namespace {{projectName}}.Validation;

/// <summary>Validates one entity, returning a message per broken rule.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this entity breaks. Empty means valid.</summary>
    IReadOnlyList<string> Validate(T item);
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
    public static string StoreEntityLookup(string projectName) => $$"""
using System;
using System.Reflection;
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
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(property?.GetValue(item) ?? 0);
    }
}
""";

    /// <summary>Source of <c>Validation/ValidatorFactory.cs</c> for <paramref name="entityNames"/>.</summary>
    public static string Factory(string projectName, IReadOnlyList<string> entityNames)
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
    public IReadOnlyList<string> Validate(T item) => Array.Empty<string>();
}
""";
    }
}
