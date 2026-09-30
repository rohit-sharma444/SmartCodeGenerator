using DomainDiscovery.Bundled;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class WordStemTests
{
    [Theory]
    [InlineData("books", "book")] [InlineData("Book", "book")] [InlineData("booking", "book")]
    [InlineData("booked", "book")] [InlineData("lends", "lend")] [InlineData("borrowing", "borrow")]
    [InlineData("enrolments", "enrolment")] [InlineData("invoice", "invoic")] [InlineData("invoicing", "invoic")]
    [InlineData("properties", "property")] [InlineData("classes", "class")] [InlineData("shopping", "shop")]
    [InlineData("billing", "bill")] [InlineData("hire", "hir")] [InlineData("hiring", "hir")]
    [InlineData("status", "status")] [InlineData("business", "business")] [InlineData("gym", "gym")]
    [InlineData("vet", "vet")] [InlineData("leases", "leas")] [InlineData("lease", "leas")]
    public void Stems(string word, string stem) => Assert.Equal(stem, WordStem.Of(word));

    [Fact]
    public void WordsSplitOnAnythingButLettersAndKeepTheWordAsTyped()
    {
        var words = WordStem.Words("A library, that LENDS books-to members!");

        Assert.Equal(new[] { "A", "library", "that", "LENDS", "books", "to", "members" },
            words.Select(w => w.Word).ToArray());
        Assert.Equal("lend", words[3].Stem);
    }
}
