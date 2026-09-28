using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The prefix is the one customization setting that can break the generated app's COMPILE, so it
/// is validated rather than ignored — unlike a malformed accent, which is dropped.
/// </summary>
[TestClass]
public class NamespacePrefixTests
{
    [TestMethod]
    public void A_plain_identifier_is_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("CodisLtd"));

    [TestMethod]
    public void Dotted_segments_are_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("Codis.Internal.Tools"));

    [TestMethod]
    public void An_underscore_start_is_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("_codis"));

    [TestMethod]
    public void Empty_is_not_a_prefix()
    {
        Assert.IsFalse(NamespacePrefix.IsValid(string.Empty));
        Assert.IsFalse(NamespacePrefix.IsValid("   "));
    }

    /// <summary>Every one of these produces source that does not compile, which is exactly why
    /// this check exists.</summary>
    [TestMethod]
    public void Malformed_prefixes_are_rejected()
    {
        Assert.IsFalse(NamespacePrefix.IsValid("3M"), "a segment may not start with a digit");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis Ltd"), "a space is not an identifier char");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis-Ltd"), "nor is a hyphen");
        Assert.IsFalse(NamespacePrefix.IsValid(".Codis"), "no empty leading segment");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis."), "no empty trailing segment");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis..Ltd"), "no empty middle segment");
        Assert.IsFalse(NamespacePrefix.IsValid("class"), "a reserved keyword is not a namespace");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis.int"), "in any segment");
    }

    /// <summary>`var` is a CONTEXTUAL keyword, so `namespace var;` compiles. Rejecting it would be
    /// stricter than C# is.</summary>
    [TestMethod]
    public void A_contextual_keyword_is_allowed()
        => Assert.IsTrue(NamespacePrefix.IsValid("var"));

    [TestMethod]
    public void A_company_becomes_an_identifier()
    {
        Assert.AreEqual("CodisLtd", NamespacePrefix.FromCompany("Codis Ltd"));
        Assert.AreEqual("SmithSons", NamespacePrefix.FromCompany("Smith & Sons"));
        Assert.AreEqual("Codis", NamespacePrefix.FromCompany("  Codis  "));
    }

    /// <summary>A suggestion that would be refused is worse than no suggestion.</summary>
    [TestMethod]
    public void A_company_that_cannot_become_one_suggests_nothing()
    {
        Assert.IsNull(NamespacePrefix.FromCompany("3M"));
        Assert.IsNull(NamespacePrefix.FromCompany("&&&"));
        Assert.IsNull(NamespacePrefix.FromCompany(string.Empty));
    }

    [TestMethod]
    public void The_default_customization_has_no_prefix()
    {
        // Null, not string.Empty: BuildAppCommand.Merge reads null as "not mentioned" so a
        // remembered profile prefix survives; EffectiveNamespacePrefix is what everything else
        // reads, and RootNamespaceFor already goes through it.
        Assert.IsNull(AppCustomization.Default.NamespacePrefix);
        Assert.AreEqual(string.Empty, AppCustomization.Default.EffectiveNamespacePrefix);
        Assert.AreEqual("Shop", AppCustomization.Default.RootNamespaceFor("Shop"));
    }

    [TestMethod]
    public void A_prefix_sits_above_the_whole_project_name()
    {
        var customization = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        Assert.AreEqual("CodisLtd.Shop", customization.RootNamespaceFor("Shop"));

        // The project name already carries the target's folder suffix, so the prefix sits above
        // the whole project identity rather than inside it.
        Assert.AreEqual("CodisLtd.Shop.Console", customization.RootNamespaceFor("Shop.Console"));
    }
}
