using AppScaffolding;
using CodeGenerator.Tests.Helper;

namespace CodeGenerator.Tests.App;

/// <summary>Branding properties are inserted with the same idempotent
/// insert-before-&lt;/Project&gt; edit InjectPackageReference and InjectSeedContent use.</summary>
[TestClass]
public class CsprojEditingTests
{
    private const string Csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
""";

    /// <summary>The same shape as <see cref="Csproj"/>, but with CRLF line endings and a
    /// pre-existing PropertyGroup holding only a RootNamespace -- what `dotnet new` or a hand
    /// edit, not this method's own LF-only insert, would leave behind.</summary>
    private const string CsprojCrlfWithRootNamespaceOnly =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <RootNamespace>CodisLtd.Shop</RootNamespace>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net10.0</TargetFramework>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>\r\n";

    /// <summary>The same shape, but the RootNamespace sits alongside another property in one
    /// CRLF PropertyGroup, so only the RootNamespace line -- not the whole group -- is removed.</summary>
    private const string CsprojCrlfWithRootNamespaceAlongsideAnotherProperty =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net10.0</TargetFramework>\r\n" +
        "    <RootNamespace>CodisLtd.Shop</RootNamespace>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>\r\n";

    /// <summary>A Blazor-shaped project file: <c>Microsoft.NET.Sdk.Web</c> globs every non-code
    /// file under the project as <c>Content</c> by default, which the plain
    /// <see cref="Csproj"/> fixture's <c>Microsoft.NET.Sdk</c> does not.</summary>
    private const string CsprojWeb = """
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
""";

    [TestMethod]
    public void InjectPackageReference_InsertsBeforeClosingTag()
    {
        var result = CsprojEditing.InjectPackageReference(Csproj, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");

        StringAssert.Contains(result,
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.10\" />");
        var closingIndex = result.IndexOf("</Project>", StringComparison.Ordinal);
        StringAssert.Contains(
            result.Substring(0, closingIndex),
            "PackageReference",
            "the reference must be inside the project element");
    }

    [TestMethod]
    public void InjectPackageReference_IsIdempotentForTheSamePackage()
    {
        var once = CsprojEditing.InjectPackageReference(Csproj, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");
        var twice = CsprojEditing.InjectPackageReference(once, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void InjectPackageReference_LeavesInputUnchangedWithoutClosingTag()
    {
        var result = CsprojEditing.InjectPackageReference("garbage without a project tag", "X", "1.0.0");

        Assert.AreEqual("garbage without a project tag", result);
    }

    [TestMethod]
    public void Branding_properties_are_inserted()
    {
        var updated = CsprojEditing.InjectProperties(Csproj, "Codis Ltd", 2026);

        StringAssert.Contains(updated, "<Company>Codis Ltd</Company>");
        StringAssert.Contains(updated, "<Authors>Codis Ltd</Authors>");
        StringAssert.Contains(updated, "<Copyright>Copyright (c) 2026 Codis Ltd.</Copyright>");
    }

    [TestMethod]
    public void Injection_is_idempotent()
    {
        var once = CsprojEditing.InjectProperties(Csproj, "Codis Ltd", 2026);
        var twice = CsprojEditing.InjectProperties(once, "Codis Ltd", 2026);

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void No_company_leaves_the_project_file_alone()
        => Assert.AreEqual(Csproj, CsprojEditing.InjectProperties(Csproj, string.Empty, 2026));

    /// <summary>An ampersand or angle bracket in a company name must not produce invalid XML.</summary>
    [TestMethod]
    public void A_company_name_is_xml_escaped()
    {
        var updated = CsprojEditing.InjectProperties(Csproj, "Smith & Sons <Ltd>", 2026);

        StringAssert.Contains(updated, "<Company>Smith &amp; Sons &lt;Ltd&gt;</Company>");
    }

    /// <summary>
    /// Update-or-insert, not insert-if-absent like the branding properties above: a stale root
    /// namespace would send every file the user adds later into a namespace no generated file
    /// declares.
    /// </summary>
    [TestMethod]
    public void A_root_namespace_is_inserted()
        => StringAssert.Contains(
            CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop"),
            "<RootNamespace>CodisLtd.Shop</RootNamespace>");

    [TestMethod]
    public void A_root_namespace_is_updated_in_place()
    {
        var once = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");
        var twice = CsprojEditing.SetRootNamespace(once, "Acme.Shop");

        StringAssert.Contains(twice, "<RootNamespace>Acme.Shop</RootNamespace>");
        Assert.IsFalse(twice.Contains("CodisLtd", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Setting_the_same_root_namespace_twice_changes_nothing()
    {
        var once = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");

        Assert.AreEqual(once, CsprojEditing.SetRootNamespace(once, "CodisLtd.Shop"));
    }

    /// <summary>Dropping the prefix has to take the element with it, or the project keeps a root
    /// namespace nothing in it declares.</summary>
    [TestMethod]
    public void An_empty_root_namespace_removes_the_element()
    {
        var set = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");
        var cleared = CsprojEditing.SetRootNamespace(set, null);

        Assert.IsFalse(cleared.Contains("RootNamespace", StringComparison.Ordinal));
    }

    [TestMethod]
    public void No_root_namespace_leaves_an_untouched_project_file_alone()
        => Assert.AreEqual(Csproj, CsprojEditing.SetRootNamespace(Csproj, null));

    [TestMethod]
    public void Removing_and_re_adding_a_root_namespace_does_not_accumulate_empty_property_groups()
    {
        // Count PropertyGroups in the original
        var initialPgCount = EmittedLines.Occurrences(Csproj, "<PropertyGroup>");

        // Set initial namespace
        var set = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");
        StringAssert.Contains(set, "<RootNamespace>CodisLtd.Shop</RootNamespace>");
        var setPgCount = EmittedLines.Occurrences(set, "<PropertyGroup>");
        Assert.IsGreaterThan(
            initialPgCount, setPgCount, "Setting namespace should add a PropertyGroup");

        // Drop the namespace
        var cleared = CsprojEditing.SetRootNamespace(set, null);
        Assert.IsFalse(cleared.Contains("RootNamespace", StringComparison.Ordinal));
        var clearedPgCount = EmittedLines.Occurrences(cleared, "<PropertyGroup>");
        Assert.AreEqual(initialPgCount, clearedPgCount,
            "Clearing namespace should remove the PropertyGroup that was added for it");

        // Re-add with a different namespace
        var reset = CsprojEditing.SetRootNamespace(cleared, "Acme.Shop");
        StringAssert.Contains(reset, "<RootNamespace>Acme.Shop</RootNamespace>");
        var resetPgCount = EmittedLines.Occurrences(reset, "<PropertyGroup>");
        Assert.AreEqual(setPgCount, resetPgCount,
            "Re-adding namespace after clearing should result in same PropertyGroup count as first add");
    }

    /// <summary>The block-removal branch's "take the whole line" check tested only `'\n'`, which on
    /// a CRLF project file is the SECOND byte of the line terminator (`'\r'` comes first) -- so the
    /// removed block's own trailing `\r\n` survived as a blank line. This does not bite a group the
    /// method inserted itself (that insert uses `\n` only), only a pre-existing CRLF one.</summary>
    [TestMethod]
    public void Clearing_a_CRLF_only_root_namespace_group_leaves_no_blank_line()
    {
        var cleared = CsprojEditing.SetRootNamespace(CsprojCrlfWithRootNamespaceOnly, null);

        Assert.IsFalse(cleared.Contains("RootNamespace", StringComparison.Ordinal));
        Assert.AreEqual(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
            + "  <PropertyGroup>\r\n"
            + "    <TargetFramework>net10.0</TargetFramework>\r\n"
            + "  </PropertyGroup>\r\n"
            + "</Project>\r\n",
            cleared);
    }

    /// <summary>The single-line removal branch has the same `'\n'`-only check, for when the
    /// PropertyGroup has to survive because it holds another property besides RootNamespace.</summary>
    [TestMethod]
    public void Clearing_a_CRLF_root_namespace_alongside_another_property_leaves_no_blank_line()
    {
        var cleared = CsprojEditing.SetRootNamespace(
            CsprojCrlfWithRootNamespaceAlongsideAnotherProperty, null);

        Assert.IsFalse(cleared.Contains("RootNamespace", StringComparison.Ordinal));
        Assert.AreEqual(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
            + "  <PropertyGroup>\r\n"
            + "    <TargetFramework>net10.0</TargetFramework>\r\n"
            + "  </PropertyGroup>\r\n"
            + "</Project>\r\n",
            cleared);
    }

    /// <summary>The desktop SDK globs no arbitrary file as <c>Content</c> by default, so the
    /// explicit <c>Include</c> is the only way the seed folder gets copied to the output
    /// directory.</summary>
    [TestMethod]
    public void Seed_content_is_included_for_the_desktop_sdk()
    {
        var updated = CsprojEditing.InjectSeedContent(Csproj);

        StringAssert.Contains(
            updated, "<Content Include=\"Data\\Seed\\*.json\" CopyToOutputDirectory=\"PreserveNewest\" />");
    }

    /// <summary>
    /// Found running a real generated Blazor app for validation-phase-3's task 15: a duplicate
    /// <c>Content</c> item is NETSDK1022, a build error no flattened-source or string-assertion
    /// test can see, because it only fires when the real SDK evaluates the project file.
    /// </summary>
    /// <remarks>
    /// <c>Microsoft.NET.Sdk.Web</c> — Blazor, and any other web target — already globs every
    /// non-code file under the project as <c>Content</c>, including <c>Data\Seed\*.json</c>. The
    /// same explicit <c>&lt;Content Include=...&gt;</c> the desktop SDK needs therefore collides
    /// with the SDK's own default item for the identical files under a Web SDK project.
    /// <c>Update</c> sets the metadata on the item the default glob already created instead of
    /// declaring a second one.
    /// </remarks>
    [TestMethod]
    public void Seed_content_is_updated_not_duplicated_for_the_web_sdk()
    {
        var updated = CsprojEditing.InjectSeedContent(CsprojWeb);

        StringAssert.Contains(
            updated, "<Content Update=\"Data\\Seed\\*.json\" CopyToOutputDirectory=\"PreserveNewest\" />");
        Assert.IsFalse(
            updated.Contains("Content Include=\"Data\\Seed", StringComparison.Ordinal),
            "an Include here duplicates the Web SDK's own default Content item (NETSDK1022)");
    }

    [TestMethod]
    public void Seed_content_injection_is_idempotent_for_the_web_sdk()
    {
        var once = CsprojEditing.InjectSeedContent(CsprojWeb);
        var twice = CsprojEditing.InjectSeedContent(once);

        Assert.AreEqual(once, twice);
    }

    /// <summary>
    /// Found running a real generated Worker app: <c>dotnet build</c> failed NETSDK1022 on
    /// <c>Data\Seed\Customer.json</c>, whatever the data source. The detection knew only the Web
    /// and Razor SDKs, but every SDK below globs <c>**\*.json</c> as <c>Content</c> by default
    /// (checked with <c>dotnet msbuild -getItem:Content</c> against SDK 10.0.401) — the Worker SDK
    /// in its own props, the rest through the static-web-assets props — so each needs
    /// <c>Update</c>, not a second <c>Include</c>.
    /// </summary>
    [TestMethod]
    [DataRow("Microsoft.NET.Sdk.Worker")]
    [DataRow("Microsoft.NET.Sdk.BlazorWebAssembly")]
    [DataRow("Microsoft.NET.Sdk.WebAssembly")]
    [DataRow("Microsoft.NET.Sdk.Razor")]
    [DataRow("Microsoft.NET.Sdk.Web")]
    public void Seed_content_is_updated_for_every_sdk_that_globs_json_as_content(string sdk)
    {
        var updated = CsprojEditing.InjectSeedContent(Csproj.Replace(
            "Sdk=\"Microsoft.NET.Sdk\"", $"Sdk=\"{sdk}\"", StringComparison.Ordinal));

        StringAssert.Contains(
            updated, "<Content Update=\"Data\\Seed\\*.json\" CopyToOutputDirectory=\"PreserveNewest\" />");
        Assert.IsFalse(
            updated.Contains("Content Include=\"Data\\Seed", StringComparison.Ordinal),
            $"{sdk} already globs the seed files as Content, so an Include is NETSDK1022");
    }

    /// <summary>The two SDKs that glob no <c>.json</c> as <c>Content</c> keep the explicit
    /// <c>Include</c>: with no default item there is nothing for <c>Update</c> to find.</summary>
    [TestMethod]
    [DataRow("Microsoft.NET.Sdk")]
    [DataRow("Microsoft.NET.Sdk.WindowsDesktop")]
    public void Seed_content_is_included_for_an_sdk_that_globs_no_json(string sdk)
    {
        var updated = CsprojEditing.InjectSeedContent(Csproj.Replace(
            "Sdk=\"Microsoft.NET.Sdk\"", $"Sdk=\"{sdk}\"", StringComparison.Ordinal));

        StringAssert.Contains(
            updated, "<Content Include=\"Data\\Seed\\*.json\" CopyToOutputDirectory=\"PreserveNewest\" />");
    }
}
