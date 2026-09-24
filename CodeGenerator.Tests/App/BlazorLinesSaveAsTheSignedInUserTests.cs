using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A Blazor editor with a login saves a transaction's lines as the user who is signed in.
/// </summary>
/// <remarks>
/// The lines are written through the store's checked save, whose permission gate reads
/// <c>ActingUser.Role()</c> - and nothing told ActingUser who was signed in to a Blazor app, which
/// has no desktop Session. Every save of an order therefore said "Saved, but could not save the
/// OrderItem lines", even one with no lines, and no line was ever written. The parent row was never
/// affected: it is saved unchecked after its own guard asks Permissions with the real role.
/// </remarks>
[TestClass]
public class BlazorLinesSaveAsTheSignedInUserTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void With_a_login_the_lines_are_saved_inside_the_signed_in_users_scope(bool validation)
    {
        var source = BlazorScreenTemplates.RecordEditor("Shop", validation, login: true);

        var scope = source.IndexOf("ActingUser.ActAs(", StringComparison.Ordinal);
        var save = source.IndexOf("lines.Save(", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, scope, "the lines are saved as nobody");
        Assert.IsLessThan(save, scope, "the scope has to be open when the lines are saved");
        StringAssert.Contains(source[scope..save], "ClaimTypes.Role", "the scope must name the real role");
    }

    /// <summary>With no login nobody is acting and the store allows that, so there is nothing to
    /// say - and a no-login app's ActingUser has no ActAs to call.</summary>
    [TestMethod]
    public void With_no_login_nothing_changes()
        => Assert.DoesNotContain(
            "ActAs(", BlazorScreenTemplates.RecordEditor("Shop", validation: true, login: false));
}
