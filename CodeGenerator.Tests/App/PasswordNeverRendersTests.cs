using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The stored hash and salt must never appear in a grid.
/// </summary>
/// <remarks>
/// Both are public strings on <c>User</c>, and every desktop pane binds a grid with
/// <c>AutoGenerateColumns="True"</c> whose only filter drops non-displayable types. So they
/// rendered as visible, EDITABLE columns: one keystroke and a Save wrote a corrupt hash and locked
/// that account out permanently, with no way back short of editing the store by hand.
/// <para>
/// The fix is <c>[Browsable(false)]</c>, which both WPF's DataGrid and Windows Forms'
/// DataGridView honour when they generate columns — so it is one attribute rather than a rule
/// duplicated into three emitted files that are already documented as needing to stay in sync.
/// </para>
/// </remarks>
[TestClass]
public class PasswordNeverRendersTests
{
    private static readonly string[] SecretProperties = ["PasswordHash", "PasswordSalt"];

    [TestMethod]
    public void The_stored_secrets_are_not_browsable()
    {
        var source = AuthModelTemplates.User("Acme");

        foreach (var name in SecretProperties)
        {
            var index = source.IndexOf($"public string {name}", StringComparison.Ordinal);

            Assert.IsGreaterThan(-1, index, $"{name} is missing entirely");

            var preceding = source[..index];

            StringAssert.EndsWith(
                preceding.TrimEnd(),
                "[Browsable(false)]",
                $"{name} must carry [Browsable(false)] or a grid will show and edit it");
        }
    }

    /// <summary>Everything a person is meant to manage stays visible; hiding the whole record
    /// would make the Users screen useless.</summary>
    [TestMethod]
    public void The_fields_an_administrator_manages_are_still_browsable()
    {
        var source = AuthModelTemplates.User("Acme");

        foreach (var name in new[] { "UserName", "DisplayName" })
        {
            var index = source.IndexOf($"public string {name}", StringComparison.Ordinal);

            Assert.IsFalse(
                source[..index].TrimEnd().EndsWith("[Browsable(false)]", StringComparison.Ordinal),
                $"{name} is meant to be visible");
        }
    }

    /// <summary>Compiles, and the attribute really is on the emitted type rather than merely in
    /// the text.</summary>
    [TestMethod]
    public void The_emitted_user_type_reports_the_attribute()
    {
        var source = EmittedSource.Flatten(
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var user = Assembly.Load(Compiler.Compile(source).AssemblyBytes!).GetType("User")!;

        foreach (var name in SecretProperties)
        {
            var browsable = user.GetProperty(name)!
                .GetCustomAttribute<System.ComponentModel.BrowsableAttribute>();

            Assert.IsNotNull(browsable, $"{name} has no BrowsableAttribute");
            Assert.IsFalse(browsable!.Browsable, $"{name} is still browsable");
        }
    }

    /// <summary>
    /// The master-detail editor builds its own field list and does NOT consult Browsable, so it
    /// needs the rule stated separately.
    /// </summary>
    /// <remarks>
    /// This is the third copy of the display convention the PropertyEditor template's own remarks
    /// warn about. A grid-only fix would have left the password editable in exactly one layout.
    /// </remarks>
    [TestMethod]
    public void The_property_editor_skips_non_browsable_fields()
        => StringAssert.Contains(
            PropertyEditorTemplate.Source("Acme"),
            "BrowsableAttribute");
}
