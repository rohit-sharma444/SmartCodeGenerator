using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public class WikitextStripperTests
{
    [Fact]
    public void Unwraps_a_piped_link_to_its_display_text()
    {
        var result = WikitextStripper.Parse("A [[cash register|till]] prints a receipt.");
        Assert.Equal("A till prints a receipt.", result.Body);
    }

    [Fact]
    public void Unwraps_a_bare_link_to_its_target()
    {
        var result = WikitextStripper.Parse("A [[receipt]] is issued.");
        Assert.Equal("A receipt is issued.", result.Body);
    }

    [Fact]
    public void Collects_link_targets_including_the_piped_form()
    {
        var result = WikitextStripper.Parse("[[Cash register|till]] and [[Receipt]].");
        Assert.Equal(new[] { "Cash register", "Receipt" }, result.Links);
    }

    [Fact]
    public void Excludes_file_category_and_image_links_from_both_prose_and_links()
    {
        var result = WikitextStripper.Parse("[[File:A.png|thumb]][[Category:Retail]]Text.");
        Assert.Equal("Text.", result.Body);
        Assert.Empty(result.Links);
    }

    [Fact]
    public void Removes_nested_templates()
    {
        var result = WikitextStripper.Parse("{{Infobox|a={{nested|x}}|b=2}}An invoice.");
        Assert.Equal("An invoice.", result.Body);
    }

    [Fact]
    public void Removes_tables_refs_and_comments()
    {
        var result = WikitextStripper.Parse(
            "{|class=\"x\"\n|a\n|}<ref>Smith 2020</ref><!-- hide -->Kept.");
        Assert.Equal("Kept.", result.Body);
    }

    [Fact]
    public void Strips_bold_and_italic_markup()
    {
        var result = WikitextStripper.Parse("An '''invoice''' is a ''document''.");
        Assert.Equal("An invoice is a document.", result.Body);
    }

    // The reason this class exists rather than reusing explaintext: PropertyMiner
    // reads these markers, and flat plain text destroys them.
    [Fact]
    public void Preserves_list_markers_as_a_line_prefix()
    {
        var result = WikitextStripper.Parse(
            "An invoice contains:\n* invoice number\n** sub item\n* date of issue");

        Assert.Equal(
            "An invoice contains:\n* invoice number\n* sub item\n* date of issue",
            result.Body);
    }

    [Fact]
    public void Drops_heading_markup_but_keeps_the_heading_text_on_its_own_line()
    {
        var result = WikitextStripper.Parse("== Contents ==\nText.");
        Assert.Equal("Contents\nText.", result.Body);
    }

    [Fact]
    public void LeadOf_returns_the_first_sentence()
    {
        Assert.Equal(
            "An invoice is a commercial document.",
            WikitextStripper.LeadOf("An invoice is a commercial document. It is issued by a seller."));
    }

    [Fact]
    public void LeadOf_is_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, WikitextStripper.LeadOf(""));
    }

    [Fact]
    public void Parse_never_throws_on_unbalanced_markup()
    {
        var result = WikitextStripper.Parse("{{unclosed [[also unclosed");
        Assert.NotNull(result.Body);
    }
}
