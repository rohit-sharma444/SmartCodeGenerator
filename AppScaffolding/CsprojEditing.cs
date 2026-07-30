namespace AppScaffolding;

/// <summary>Minimal text edits to a project file's XML, mirroring
/// <see cref="TestProjectTemplates.InjectProjectReference"/>.</summary>
public static class CsprojEditing
{
    /// <summary>Inserts a <c>PackageReference</c> for <paramref name="packageId"/> just before the
    /// closing <c>&lt;/Project&gt;</c> tag. Idempotent: returns <paramref name="csproj"/> unchanged
    /// if it already references the package or has no closing tag.</summary>
    public static string InjectPackageReference(string csproj, string packageId, string version)
    {
        if (csproj.Contains($"Include=\"{packageId}\""))
            return csproj;

        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <PackageReference Include=\"{packageId}\" Version=\"{version}\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }

    /// <summary>
    /// Registers the seed folder as content copied to the output directory, so the generated app
    /// can find <c>Data/Seed/&lt;Class&gt;.json</c> beside its binary at run time. Idempotent, and
    /// a no-op on a project file with no closing tag.
    /// </summary>
    public static string InjectSeedContent(string csproj)
    {
        const string include = @"Data\Seed\*.json";

        if (csproj.Contains(include, StringComparison.Ordinal))
            return csproj;

        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <Content Include=\"{include}\" CopyToOutputDirectory=\"PreserveNewest\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
}
