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
    /// <remarks>
    /// <c>Update</c>, not <c>Include</c>, when the project's own SDK is a Web one.
    /// <c>Microsoft.NET.Sdk.Web</c> — Blazor, and any other web target — globs every non-code
    /// file under the project as <c>Content</c> by default, so an explicit <c>Include</c> for the
    /// same files is a duplicate item: NETSDK1022, a build error a flattened-source or
    /// string-assertion test cannot see, because it only fires once the real SDK evaluates the
    /// project file. The desktop SDK (WPF, WinForms) globs no such thing, so it still needs the
    /// explicit <c>Include</c> — without the default glob there is no pre-existing item for
    /// <c>Update</c> to find.
    /// </remarks>
    public static string InjectSeedContent(string csproj)
    {
        const string include = @"Data\Seed\*.json";
        var attribute = IsWebSdk(csproj) ? "Update" : "Include";

        return InjectItem(
            csproj, include,
            $"    <Content {attribute}=\"{include}\" CopyToOutputDirectory=\"PreserveNewest\" />\n",
            attribute);
    }

    /// <summary>Whether <paramref name="csproj"/> declares a Web-flavoured SDK
    /// (<c>Microsoft.NET.Sdk.Web</c>, <c>Microsoft.NET.Sdk.Razor</c>) — the ones that glob
    /// arbitrary files as <c>Content</c> by default.</summary>
    private static bool IsWebSdk(string csproj)
    {
        var open = csproj.IndexOf("<Project Sdk=\"", StringComparison.Ordinal);
        if (open < 0)
            return false;

        var start = open + "<Project Sdk=\"".Length;
        var end = csproj.IndexOf('"', start);
        if (end < 0)
            return false;

        var sdk = csproj[start..end];
        return sdk.Contains(".Web", StringComparison.OrdinalIgnoreCase)
            || sdk.Contains(".Razor", StringComparison.OrdinalIgnoreCase);
    }

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
    private static string InjectItem(
        string csproj, string include, string item, string attribute = "Include")
    {
        if (csproj.Contains($"{attribute}=\"{include}\"", StringComparison.Ordinal))
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
                // Check if we can remove the PropertyGroup wrapper too, to avoid leaving it empty.
                // Matching the exact shape is safe regardless of who wrote this group -- this
                // method, InjectProperties/InjectBranding/InjectPackageReference (which write
                // structurally identical groups), or a user by hand -- because the guard below only
                // fires when the group holds nothing but this RootNamespace element.
                var propertyGroupOpen = "<PropertyGroup>";
                var propertyGroupClose = "</PropertyGroup>";

                var pgOpenIndex = csproj.LastIndexOf(propertyGroupOpen, open, StringComparison.Ordinal);

                if (pgOpenIndex >= 0)
                {
                    var pgCloseIndex = csproj.IndexOf(propertyGroupClose, end, StringComparison.Ordinal);

                    if (pgCloseIndex >= 0)
                    {
                        // Check if the PropertyGroup contains only this RootNamespace element (and whitespace).
                        var beforeElement = csproj.Substring(
                            pgOpenIndex + propertyGroupOpen.Length,
                            open - pgOpenIndex - propertyGroupOpen.Length);
                        var afterElement = csproj.Substring(end, pgCloseIndex - end);

                        // Both before and after should be whitespace only.
                        if (beforeElement.Trim().Length == 0 && afterElement.Trim().Length == 0)
                        {
                            // Remove the entire PropertyGroup block, taking the whole lines.
                            var blockStart = csproj.LastIndexOf('\n', pgOpenIndex) + 1;
                            var blockEnd = ConsumeLineEnding(csproj, pgCloseIndex + propertyGroupClose.Length);

                            return csproj[..blockStart] + csproj[blockEnd..];
                        }
                    }
                }

                // Just remove the RootNamespace line if PropertyGroup contains other elements.
                var lineStart = csproj.LastIndexOf('\n', open) + 1;
                var lineEnd = ConsumeLineEnding(csproj, end);

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

    /// <summary>
    /// <paramref name="position"/> moved past the line terminator that starts there, or left
    /// unchanged if there is none. Handles both `\n` (this method's own inserts, and any
    /// LF-only file) and `\r\n` (what `dotnet new` and most hand edits on Windows write) — a
    /// plain `== '\n'` check sees only the second byte of a CRLF terminator and leaves the `\r`
    /// behind as a blank line.
    /// </summary>
    private static int ConsumeLineEnding(string text, int position)
    {
        if (position + 1 < text.Length && text[position] == '\r' && text[position + 1] == '\n')
            return position + 2;

        if (position < text.Length && (text[position] == '\n' || text[position] == '\r'))
            return position + 1;

        return position;
    }

    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
