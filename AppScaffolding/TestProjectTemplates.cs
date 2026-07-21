namespace AppScaffolding;

/// <summary>Emits the sources of the companion <c>&lt;app&gt;.Tests</c> xUnit project, and the
/// one edit needed to point its .csproj at the app project.</summary>
public static class TestProjectTemplates
{
    /// <summary>A self-cleaning CRUD round-trip over each entity's typed repository. Asserts on the
    /// row it creates (not on store counts), so the tests tolerate seeded data and each other.</summary>
    public static string RoundTripTests(string projectName, IReadOnlyList<string> entityNames)
    {
        var facts = string.Join("\n\n", entityNames.Select(name => $$"""
    [Fact]
    public void {{name}}_round_trips() => RoundTrip(new {{name}}Repository());
"""));

        return $$"""
using System.Reflection;
using {{projectName}}.Data;
using {{projectName}}.Models;
using Xunit;

namespace {{projectName}}.Tests;

/// <summary>CRUD round-trip over each entity's typed repository. Each test creates a row and
/// deletes it, asserting on that row rather than on store counts.</summary>
public class RepositoryRoundTripTests
{
{{facts}}

    private static void RoundTrip<T>(Repository<T> repository) where T : class, new()
    {
        var created = repository.Create(new T());
        var id = IdOf(created);
        Assert.NotEqual(0, id);
        Assert.NotNull(repository.GetById(id));
        Assert.True(repository.Update(created));
        Assert.True(repository.Delete(id));
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

    /// <summary>Asserts EnsureSeeded populates the first entity's store and is idempotent.</summary>
    public static string SeedTests(string projectName, string firstEntityName) => $$"""
using {{projectName}}.Data;
using Xunit;

namespace {{projectName}}.Tests;

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
