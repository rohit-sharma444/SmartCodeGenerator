using Xunit;

namespace DataIngestion.Tests;

public class ConnectionSecretTests
{
    [Fact]
    public void A_password_is_replaced_and_the_rest_is_left_alone()
    {
        var redacted = ConnectionSecret.Redact(
            "Server=.;Database=Northwind;User ID=sa;Password=hunter2");

        Assert.DoesNotContain("hunter2", redacted);
        Assert.Contains("Password=***", redacted);
        Assert.Contains("Server=.", redacted);
        Assert.Contains("Database=Northwind", redacted);
        Assert.Contains("User ID=sa", redacted);
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("PWD")]
    [InlineData("PASSWORD")]
    public void Every_spelling_SqlClient_accepts_is_redacted(string key)
    {
        var redacted = ConnectionSecret.Redact($"Server=.;{key}=hunter2");

        Assert.DoesNotContain("hunter2", redacted);
    }

    // A password may legally contain a semicolon, in which case it is quoted. Splitting naively
    // on ';' would leave the tail of the password in the output.
    [Fact]
    public void A_password_containing_a_semicolon_is_redacted_whole()
    {
        var redacted = ConnectionSecret.Redact("Server=.;Password=\"a;b;c\";Database=X");

        Assert.DoesNotContain("a;b;c", redacted);
        Assert.Contains("Database=X", redacted);
    }

    // The overwhelmingly common case in the environments this is used in. Rewriting a string that
    // holds no secret would make the replay line differ from what the user typed for no reason.
    [Fact]
    public void A_trusted_connection_is_returned_unchanged()
    {
        const string original = "Server=.;Database=Northwind;Trusted_Connection=True";

        Assert.Equal(original, ConnectionSecret.Redact(original));
    }

    [Fact]
    public void Something_that_is_not_a_connection_string_is_returned_unchanged()
    {
        const string original = @"D:\data\orders.csv";

        Assert.Equal(original, ConnectionSecret.Redact(original));
    }

    /// <summary>
    /// The replay line has to stay something the user recognises as what they typed. Rebuilding
    /// through DbConnectionStringBuilder lower-cases every key, so Database=Northwind comes back
    /// as database=Northwind - which invites the question of what else was changed.
    /// </summary>
    [Fact]
    public void Every_other_key_keeps_the_casing_and_spacing_it_had()
    {
        var redacted = ConnectionSecret.Redact(
            "Server=.;Initial Catalog=Northwind;Password=hunter2;MultipleActiveResultSets=True");

        Assert.Contains("Initial Catalog=Northwind", redacted);
        Assert.Contains("MultipleActiveResultSets=True", redacted);
        Assert.Contains("Server=.", redacted);
    }

    /// <summary>
    /// The first hit for "password" here is inside a VALUE, and only the real key must be masked.
    /// What rejects the decoy is that the character after it is not '=' — remove that test and
    /// this database's name is destroyed.
    /// </summary>
    [Fact]
    public void A_value_that_merely_contains_the_key_name_is_left_alone()
    {
        var redacted = ConnectionSecret.Redact(
            "Server=.;Database=MyPasswordDb;Password=hunter2");

        Assert.Contains("Database=MyPasswordDb", redacted);
        Assert.Contains("Password=***", redacted);
        Assert.DoesNotContain("hunter2", redacted);
    }

    /// <summary>
    /// The decoy that actually matters: "pwd" inside an earlier VALUE. Scanning forward from it
    /// for the next '=' lands on the NEXT KEY's assignment, so a naive splice masks that key's
    /// value and leaves the real password untouched — the worst of both outcomes.
    /// </summary>
    [Fact]
    public void A_key_name_hidden_in_an_earlier_value_does_not_move_the_splice()
    {
        var redacted = ConnectionSecret.Redact("Server=xpwdy;Database=A;Pwd=secret");

        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("Database=A", redacted);
        Assert.Contains("Server=xpwdy", redacted);
    }

    // The history file used to keep the password in plain text: the whole typed line was written
    // as it was, and `history` printed it back.
    [Fact]
    public void A_typed_command_line_keeps_everything_but_the_password()
    {
        var line = ConnectionSecret.RedactLine(
            "build app: a shop from sql \"Server=.;Database=Shop;User Id=u;Password=secret123;Connect Timeout=2\" tables Customer");

        Assert.DoesNotContain("secret123", line);
        Assert.Equal(
            "build app: a shop from sql \"Server=.;Database=Shop;User Id=u;Password=***;Connect Timeout=2\" tables Customer",
            line);
    }

    [Fact]
    public void A_password_last_in_the_quoted_string_stops_at_the_quote()
    {
        var line = ConnectionSecret.RedactLine(
            "build app: trading from sql \"Server=.;Database=T;PWD=hunter2\" tables A,B");

        Assert.DoesNotContain("hunter2", line);
        Assert.EndsWith("PWD=***\" tables A,B", line);
    }

    [Theory]
    [InlineData("create project Shop")]
    [InlineData(@"build app: a library from csv C:\data\books.csv")]
    [InlineData("build app: trading from sql \"Server=.;Database=T;Trusted_Connection=True\"")]
    public void A_line_with_no_password_is_left_exactly_as_typed(string typed)
        => Assert.Equal(typed, ConnectionSecret.RedactLine(typed));
}
