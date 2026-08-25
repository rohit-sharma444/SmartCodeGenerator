using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Scanning C# for the code block.
/// </summary>
/// <remarks>
/// One test here outranks the others: the spans must concatenate back to exactly what went in. A
/// highlighter that drops a character is a highlighter that shows the user code they do not have,
/// and no amount of correct colouring makes up for it.
/// </remarks>
[TestClass]
public class CSharpTokensTests
{
    [TestMethod]
    public void Nothing_is_lost()
    {
        var code = """
            using System;

            namespace Acme.Shop;

            /// <summary>An order.</summary>
            public sealed class Order
            {
                // 42 is not a magic number if you name it
                public const int MaxLines = 42;

                public string Reference { get; set; } = "ORD-000";

                public char Separator = '-';

                public decimal Total(decimal net) => net * 1.20m;
            }
            """;

        var scanned = CSharpTokens.Scan(code);

        Assert.AreEqual(code, string.Concat(scanned.Select(t => t.Text)));
    }

    [TestMethod]
    public void Nothing_is_lost_from_awkward_input()
    {
        foreach (var code in new[]
                 {
                     "",
                     "\"unterminated",
                     "// trailing comment with no newline",
                     "/* unterminated block",
                     "'",
                     "\"escaped \\\" quote\"",
                     "123abc",
                     "\r\n\r\n",
                 })
        {
            Assert.AreEqual(
                code,
                string.Concat(CSharpTokens.Scan(code).Select(t => t.Text)),
                $"lost something scanning: {code}");
        }
    }

    [TestMethod]
    public void Keywords_strings_comments_and_numbers_are_marked()
    {
        var scanned = CSharpTokens.Scan("public int X = 42; // note\n");

        Assert.Contains(new Token(TokenKind.Keyword, "public"), scanned);
        Assert.Contains(new Token(TokenKind.Keyword, "int"), scanned);
        Assert.Contains(new Token(TokenKind.Number, "42"), scanned);
        Assert.IsTrue(scanned.Any(t => t.Kind == TokenKind.Comment && t.Text.Contains("note")));
    }

    [TestMethod]
    public void A_word_that_merely_starts_with_a_keyword_is_not_one()
    {
        // "internalName" is an identifier. Matching keywords by prefix rather than by whole word
        // is the classic way a highlighter starts colouring variable names.
        var scanned = CSharpTokens.Scan("var internalName = 1;");

        Assert.IsFalse(scanned.Any(t => t.Kind == TokenKind.Keyword && t.Text == "internal"));
        Assert.Contains(new Token(TokenKind.Plain, "internalName"), scanned);
    }

    [TestMethod]
    public void A_keyword_inside_a_string_stays_part_of_the_string()
    {
        var scanned = CSharpTokens.Scan("""var s = "public";""");

        Assert.IsFalse(scanned.Any(t => t.Kind == TokenKind.Keyword && t.Text == "public"));
        Assert.IsTrue(scanned.Any(t => t.Kind == TokenKind.Text && t.Text.Contains("public")));
    }
}
