using AppScaffolding;

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
}
