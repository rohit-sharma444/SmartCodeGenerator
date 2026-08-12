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
        => InjectContent(csproj, @"Data\Seed\*.json");

    /// <summary>
    /// Registers <paramref name="include"/> as content copied next to the built binary.
    /// Idempotent, and a no-op on a project file with no closing tag.
    /// </summary>
    public static string InjectContent(string csproj, string include)
        => InjectItem(
            csproj, include,
            $"    <Content Include=\"{include}\" CopyToOutputDirectory=\"PreserveNewest\" />\n");

    /// <summary>
    /// Registers <paramref name="include"/> as a WPF resource, embedded in the assembly where a
    /// relative pack URI — a <c>Window</c>'s <c>Icon</c> attribute, say — can reach it.
    /// Idempotent, and a no-op on a project file with no closing tag.
    /// </summary>
    public static string InjectResource(string csproj, string include)
        => InjectItem(csproj, include, $"    <Resource Include=\"{include}\" />\n");

    /// <summary>Sets <c>&lt;ApplicationIcon&gt;</c> — the icon the built .exe itself carries — to
    /// <paramref name="relativePath"/>. Idempotent, and a no-op on a project file with no closing
    /// tag.</summary>
    public static string InjectApplicationIcon(string csproj, string relativePath)
    {
        if (csproj.Contains("<ApplicationIcon>", StringComparison.Ordinal))
            return csproj;

        var group =
            "  <PropertyGroup>\n" +
            $"    <ApplicationIcon>{relativePath}</ApplicationIcon>\n" +
            "  </PropertyGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, group);
    }

    /// <summary>Inserts <paramref name="item"/> in its own ItemGroup before the closing
    /// <c>&lt;/Project&gt;</c> tag, unless something already includes
    /// <paramref name="include"/>.</summary>
    /// <remarks>
    /// The guard matches the <c>Include</c> attribute rather than the bare path, because the same
    /// path can legitimately appear elsewhere in the file: an icon registered as
    /// <c>&lt;ApplicationIcon&gt;Assets\app.ico&lt;/ApplicationIcon&gt;</c> must not stop the
    /// <c>Resource</c> item for that same file being added.
    /// </remarks>
    private static string InjectItem(string csproj, string include, string item)
    {
        if (csproj.Contains($"Include=\"{include}\"", StringComparison.Ordinal))
            return csproj;

        var itemGroup = "  <ItemGroup>\n" + item + "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }

    /// <summary>
    /// Adds the branding properties for <paramref name="company"/> before the closing
    /// <c>&lt;/Project&gt;</c> tag. Idempotent, a no-op when there is no company, and a no-op on
    /// a project file with no closing tag.
    /// </summary>
    /// <remarks>
    /// <paramref name="year"/> is a parameter rather than read from the clock, so a golden
    /// project file does not change on 1 January. The company is XML-escaped: "Smith &amp; Sons"
    /// would otherwise produce a project file MSBuild cannot parse.
    /// </remarks>
    public static string InjectProperties(string csproj, string company, int year)
    {
        if (company.Length == 0 || csproj.Contains("<Company>", StringComparison.Ordinal))
            return csproj;

        var escaped = Escape(company);

        var group =
            "  <PropertyGroup>\n" +
            $"    <Company>{escaped}</Company>\n" +
            $"    <Authors>{escaped}</Authors>\n" +
            $"    <Copyright>Copyright (c) {year} {escaped}.</Copyright>\n" +
            "  </PropertyGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, group);
    }

    private const string RootNamespaceOpen = "<RootNamespace>";
    private const string RootNamespaceClose = "</RootNamespace>";

    /// <summary>
    /// Sets <c>&lt;RootNamespace&gt;</c> to <paramref name="rootNamespace"/>, replacing any
    /// existing value; a null or empty value removes the element. A no-op on a project file with
    /// no closing tag.
    /// </summary>
    /// <remarks>
    /// Update-or-insert rather than the insert-if-absent <see cref="InjectProperties"/> uses,
    /// because this value can go stale in a way that misleads the IDE: re-scaffolding an existing
    /// project under a different prefix would otherwise leave every file the user adds afterwards
    /// in a namespace no generated file declares. Removing it on an empty value matters for the
    /// same reason, in reverse.
    /// </remarks>
    public static string SetRootNamespace(string csproj, string? rootNamespace)
    {
        var open = csproj.IndexOf(RootNamespaceOpen, StringComparison.Ordinal);

        if (open >= 0)
        {
            var close = csproj.IndexOf(RootNamespaceClose, open, StringComparison.Ordinal);

            if (close < 0)
                return csproj;

            var end = close + RootNamespaceClose.Length;

            if (string.IsNullOrEmpty(rootNamespace))
            {
                // Take the whole line, so removing the element does not leave a blank one behind.
                var lineStart = csproj.LastIndexOf('\n', open) + 1;
                var lineEnd = end < csproj.Length && csproj[end] == '\n' ? end + 1 : end;

                return csproj[..lineStart] + csproj[lineEnd..];
            }

            return csproj[..open] + RootNamespaceOpen + rootNamespace + csproj[close..];
        }

        if (string.IsNullOrEmpty(rootNamespace))
            return csproj;

        var group =
            "  <PropertyGroup>\n" +
            $"    {RootNamespaceOpen}{rootNamespace}{RootNamespaceClose}\n" +
            "  </PropertyGroup>\n";

        var closeProject = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return closeProject < 0 ? csproj : csproj.Insert(closeProject, group);
    }

    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
