using WikipediaDumpImporter;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public class DumpPageReaderTests
{
    private static List<RawPage> ReadFixture()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-dump.xml"));
        return DumpPageReader.ReadXml(stream).ToList();
    }

    [Fact]
    public void Reads_every_page_including_the_non_mainspace_one()
    {
        // Filtering by namespace is ImportRunner's job, not the reader's: the reader
        // reports what the dump says, so the filter stays testable on its own.
        Assert.Equal(3, ReadFixture().Count);
    }

    [Fact]
    public void Reads_a_pages_id_title_and_namespace()
    {
        var invoice = ReadFixture().Single(p => p.Title == "Invoice");

        Assert.Equal(101, invoice.Id);
        Assert.Equal(0, invoice.Namespace);
    }

    [Fact]
    public void Reads_the_wikitext_body()
    {
        var invoice = ReadFixture().Single(p => p.Title == "Invoice");

        Assert.Contains("* invoice number", invoice.Wikitext);
    }

    [Fact]
    public void Reports_a_redirects_target()
    {
        var bill = ReadFixture().Single(p => p.Title == "Bill (payment)");

        Assert.Equal("Invoice", bill.RedirectTo);
    }

    [Fact]
    public void An_article_has_no_redirect_target()
    {
        Assert.Null(ReadFixture().Single(p => p.Title == "Invoice").RedirectTo);
    }

    [Fact]
    public void Reports_a_non_mainspace_namespace_as_itself()
    {
        Assert.Equal(1, ReadFixture().Single(p => p.Title == "Talk:Invoice").Namespace);
    }
}
