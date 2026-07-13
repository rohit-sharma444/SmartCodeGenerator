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

    [Fact]
    public void Drops_a_file_link_whole_even_when_its_caption_contains_a_link()
    {
        // The naive `IndexOf("]]")` scan used to grab the INNER close, so the caption tail and a
        // stray literal "]]" leaked into prose. Depth-counting must find the true outer close.
        var result = WikitextStripper.Parse(
            "[[File:till.jpg|thumb|A [[receipt]] printed by the till.]]The register.");

        Assert.Equal("The register.", result.Body);
    }

    [Fact]
    public void Recovers_a_link_nested_inside_a_dropped_file_links_caption()
    {
        // Decision: the File: link itself is still not prose (its caption is not shown), but the
        // article DOES link to "receipt" from that caption, so the target must not be lost.
        var result = WikitextStripper.Parse(
            "[[File:till.jpg|thumb|A [[receipt]] printed by the till.]]The register.");

        Assert.Equal(new[] { "receipt" }, result.Links);
    }

    [Fact]
    public void Unwraps_an_external_link_to_its_display_text()
    {
        var result = WikitextStripper.Parse("Visit the [http://example.com official site].");
        Assert.Equal("Visit the official site.", result.Body);
    }

    [Fact]
    public void Drops_a_bare_external_link_entirely()
    {
        var result = WikitextStripper.Parse("Cited elsewhere.[http://example.com]");
        Assert.Equal("Cited elsewhere.", result.Body);
    }

    [Fact]
    public void External_links_never_contribute_to_links()
    {
        var result = WikitextStripper.Parse(
            "Visit the [http://example.com official site] and read [[Receipt]].");
        Assert.Equal(new[] { "Receipt" }, result.Links);
    }

    [Fact]
    public void Strips_definition_list_markers_without_treating_them_as_bullets()
    {
        // ':' and ';' are indentation/definition markers, not enumerations: PropertyMiner must not
        // see them as list items, unlike genuine '*'/'#' bullets.
        var result = WikitextStripper.Parse(";Term\n:Definition of the term.");
        Assert.Equal("Term\nDefinition of the term.", result.Body);
    }
}
