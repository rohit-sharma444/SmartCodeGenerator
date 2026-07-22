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
}
