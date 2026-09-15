namespace AppScaffolding;

/// <summary>Emits the sources of the companion <c>&lt;app&gt;.Tests</c> xUnit project, and the
/// one edit needed to point its .csproj at the app project.</summary>
public static class TestProjectTemplates
{
    /// <summary>A self-cleaning CRUD round-trip over each entity's typed repository. Asserts on the
    /// row it creates (not on store counts), so the tests tolerate seeded data and each other.</summary>
    public static string RoundTripTests(string rootNamespace, IReadOnlyList<string> entityNames)
    {
        var facts = string.Join("\n\n", entityNames.Select(name => $$"""
    [Fact]
    public void {{name}}_round_trips() => RoundTrip(new {{name}}Repository());
"""));

        return $$"""
using System.Reflection;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>CRUD round-trip over each entity's typed repository. Each test creates a row and
/// deletes it, asserting on that row rather than on store counts.</summary>
public class RepositoryRoundTripTests
{
{{facts}}

    private static void RoundTrip<T>(Repository<T> repository) where T : class, new()
    {
        // A blank record no longer round-trips: the generated validators reject it. TestSupport
        // fills it with values of the shape those rules accept.
        var item = new T();
        TestSupport.Fill(item);

        var created = repository.Create(item);
        Assert.True(created.Success, string.Join("; ", created.Errors));

        var id = IdOf(created.Item!);
        Assert.NotEqual(0, id);
        Assert.NotNull(repository.GetById(id));

        var updated = repository.Update(created.Item!);
        Assert.True(updated.Success, string.Join("; ", updated.Errors));

        var deleted = repository.Delete(id);
        Assert.True(deleted.Success, string.Join("; ", deleted.Errors));
        Assert.Null(repository.GetById(id));
    }

    private static int IdOf<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }

    /// <summary>Source of <c><paramref name="entityName"/>ValidatorTests.cs</c>. When the model has
    /// a required property, also asserts a blank record is rejected — otherwise the only honest
    /// assertion is that a filled record passes.</summary>
    public static string ValidatorTests(string rootNamespace, string entityName, string? requiredPropertyName)
    {
        var requiredFact = requiredPropertyName is null ? string.Empty : $$"""


    [Fact]
    public void A_blank_{{entityName}}_reports_its_required_field()
    {
        var context = ValidationContext<{{entityName}}>.For(
            new {{entityName}}(), ValidationOperation.Insert, new AlwaysFound());
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.Contains("{{requiredPropertyName}} is required.", errors);
    }
""";

        return $$"""
using {{rootNamespace}}.Models;
using {{rootNamespace}}.Validation;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>Rules generated for {{entityName}}. Uses a fake lookup so no store is needed.</summary>
public class {{entityName}}ValidatorTests
{
    /// <summary>Every foreign key resolves, so only the property rules are under test.</summary>
    private sealed class AlwaysFound : IEntityLookup
    {
        public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

        public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
            where TEntity : class, new() => 0;
    }

    [Fact]
    public void A_populated_{{entityName}}_is_valid()
    {
        var item = new {{entityName}}();
        TestSupport.Fill(item);

        var context = ValidationContext<{{entityName}}>.For(item, ValidationOperation.Insert, new AlwaysFound());
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.Empty(errors);
    }

    [Fact]
    public void A_null_{{entityName}}_is_reported_rather_than_throwing()
    {
        var context = ValidationContext<{{entityName}}>.For(null!, ValidationOperation.Insert, new AlwaysFound());
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.NotEmpty(errors);
    }
{{requiredFact}}
}
""";
    }

    /// <summary>Source of <c>TestSupport.cs</c>: entity population shared by the generated
    /// round-trip and validator tests.</summary>
    /// <remarks>
    /// A foreign-key-shaped int property now gets a REAL row's Id, not a placeholder: since the
    /// Entity stage checks a Transaction's or a DocumentLine's foreign key against the real store
    /// rather than skipping it at zero, a fixture that left it at 0 would fail its own generated
    /// validator. <c>ForeignKeyValue</c> reuses the referenced type's own repository -- reading an
    /// existing row when the store already has one, creating one (recursively filled the same way)
    /// when it does not -- so this works whether or not the app seeds sample data.
    /// </remarks>
    public static string TestSupport(string rootNamespace) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace {{rootNamespace}}.Tests;

/// <summary>Builds entities that satisfy the generated validation rules.</summary>
internal static class TestSupport
{
    /// <summary>Types currently being created for a caller further up the same call stack, so a
    /// foreign key that forms a cycle stops recursing instead of overflowing the stack. Never
    /// expected to hold more than one entry in a generated app, whose models describe no cycle.</summary>
    [ThreadStatic]
    private static HashSet<string>? _creating;

    /// <summary>Populates every writable property with a value the validators accept.</summary>
    public static void Fill<T>(T item)
    {
        // The model's own assembly and namespace, not TestSupport's: T (e.g. Shop.Models.Order)
        // lives in the APP assembly, and that is where {Target}Repository and {Target} itself are
        // declared too -- TestSupport's own assembly is the separate Tests project and reflecting
        // against it would never find either type.
        var appAssembly = typeof(T).Assembly;
        var root = typeof(T).Namespace![..^".Models".Length];

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;

            if (property.PropertyType == typeof(string))
                property.SetValue(item, Placeholder(property.Name));
            else if (property.PropertyType == typeof(DateTime))
                property.SetValue(item, DateTime.UtcNow);
            else if (property.PropertyType == typeof(int) && IsForeignKey(property.Name))
                property.SetValue(item, ForeignKeyValue(property.Name, appAssembly, root));
        }
    }

    /// <summary>A value of the right shape for this property. A property the validator treats as
    /// an email or a phone number needs a value of that shape, so a plain placeholder will not do.</summary>
    public static string Placeholder(string propertyName)
    {
        if (propertyName.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "test@example.com";

        if (propertyName.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return "+1 555 0100";

        return "Test";
    }

    /// <summary>Whether <paramref name="propertyName"/> is shaped like a foreign key -- the same
    /// name test AppScaffolding.ForeignKeys applies when it decides what the generated validators
    /// check.</summary>
    private static bool IsForeignKey(string propertyName)
        => propertyName.Length > 2
           && propertyName.EndsWith("Id", StringComparison.Ordinal)
           && !string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Id of a real row of the type <paramref name="propertyName"/> references --
    /// an existing one if the store already has one, else one created through that type's own
    /// repository (and, recursively, Filled the same way this item was). Neither type existing by
    /// that name (an incidental "...Id" that names no entity) nor the creation failing is fatal:
    /// both fall back to 0, exactly what this property held before this method existed.</summary>
    private static int ForeignKeyValue(string propertyName, Assembly appAssembly, string root)
    {
        var targetName = propertyName[..^2];

        var repositoryType = appAssembly.GetType(root + ".Data." + targetName + "Repository");
        if (repositoryType is null)
            return 0;

        _creating ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_creating.Add(targetName))
            return 0;

        try
        {
            var repository = Activator.CreateInstance(repositoryType)!;

            var existing = (IEnumerable)repositoryType.GetMethod("GetAll")!.Invoke(repository, null)!;
            foreach (var row in existing)
                return IdOf(row);

            var modelType = appAssembly.GetType(root + ".Models." + targetName);
            if (modelType is null)
                return 0;

            var target = Activator.CreateInstance(modelType)!;
            typeof(TestSupport).GetMethod(nameof(Fill))!.MakeGenericMethod(modelType)
                .Invoke(null, new[] { target });

            var created = repositoryType.GetMethod("Create")!.Invoke(repository, new[] { target })!;
            if (!(bool)created.GetType().GetProperty("Success")!.GetValue(created)!)
                return 0;

            var createdItem = created.GetType().GetProperty("Item")!.GetValue(created);
            return createdItem is null ? 0 : IdOf(createdItem);
        }
        finally
        {
            _creating.Remove(targetName);
        }
    }

    private static int IdOf(object item)
    {
        var id = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";

    /// <summary>Asserts EnsureSeeded populates the first entity's store and is idempotent.</summary>
    public static string SeedTests(string rootNamespace, string firstEntityName) => $$"""
using {{rootNamespace}}.Data;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>SeedData populates empty stores on first run and does nothing on the second.</summary>
public class SeedDataTests
{
    [Fact]
    public void EnsureSeeded_populates_stores_and_is_idempotent()
    {
        SeedData.EnsureSeeded();
        var afterFirst = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.True(afterFirst > 0);

        SeedData.EnsureSeeded();
        var afterSecond = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.Equal(afterFirst, afterSecond);
    }
}
""";

    /// <summary>Disables xUnit parallelization: the JSON stores are shared files, so the tests
    /// run sequentially to stay deterministic.</summary>
    public static string DisableParallelization() => """
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
""";

    /// <summary>Inserts a ProjectReference to the app project into a freshly-generated test
    /// .csproj, just before the closing &lt;/Project&gt; tag.</summary>
    public static string InjectProjectReference(string csproj, string relativeCsprojPath)
    {
        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"{relativeCsprojPath}\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
}
