using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Windows Forms shares every view model with WPF; only the view and the entry point
/// differ, so those are what these tests pin.</summary>
[TestClass]
public class WinFormsLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new WinFormsTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_winforms_app_emits_a_login_form()
        => CollectionAssert.Contains(
            Files(AppCustomization.Default).Select(f => f.Name).ToList(), "LoginForm.cs");

    [TestMethod]
    public void Nologin_emits_no_login_form()
        => CollectionAssert.DoesNotContain(
            Files(AppCustomization.Default with { Login = false }).Select(f => f.Name).ToList(),
            "LoginForm.cs");

    [TestMethod]
    public void The_entry_point_runs_the_login_before_the_shell()
    {
        var program = SourceOf(AppCustomization.Default, "Program.cs");

        var login = program.IndexOf("new LoginForm()", StringComparison.Ordinal);
        var shell = program.IndexOf("new MainForm()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, login, "the login form is never constructed");
        Assert.IsLessThan(shell, login, "the shell must not be built before the sign-in");
    }

    /// <summary>Application.Run must not be given the login form: running it as the message loop
    /// would end the application when it closed.</summary>
    [TestMethod]
    public void The_message_loop_runs_the_shell_not_the_login()
    {
        var program = SourceOf(AppCustomization.Default, "Program.cs");

        StringAssert.Contains(program, "ShowDialog()");
        Assert.IsFalse(program.Contains("Application.Run(new LoginForm", StringComparison.Ordinal));
    }

    /// <summary>The regression proof for the entry point: with `nologin` it is what it always
    /// was, byte for byte.</summary>
    [TestMethod]
    public void Nologin_leaves_the_entry_point_byte_identical()
        => Assert.AreEqual(
            WinFormsProgramTemplate.Source("Shop"),
            SourceOf(AppCustomization.Default with { Login = false }, "Program.cs"));

    [TestMethod]
    public void Signing_out_returns_to_the_login()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "Program.cs"), "Session.SignOutRequested");

    [TestMethod]
    public void The_entry_point_seeds_the_administrator_before_asking()
    {
        var program = SourceOf(AppCustomization.Default, "Program.cs");

        Assert.IsLessThan(
            program.IndexOf("new LoginForm()", StringComparison.Ordinal),
            program.IndexOf("UserSeed.EnsureAdminSeeded();", StringComparison.Ordinal));
    }

    /// <summary>The password character must be set, or the password is typed in clear.</summary>
    [TestMethod]
    public void The_password_box_masks_what_is_typed()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "LoginForm.cs"), "UseSystemPasswordChar = true");

    /// <summary>AcceptButton is how Enter submits a Windows Forms dialog.</summary>
    [TestMethod]
    public void Enter_signs_in()
        => StringAssert.Contains(SourceOf(AppCustomization.Default, "LoginForm.cs"), "AcceptButton");

    [TestMethod]
    public void The_brand_panel_shows_the_company_when_there_is_one()
    {
        var branded = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        StringAssert.Contains(SourceOf(branded, "LoginForm.cs"), "Codis Ltd");
    }

    /// <summary>A quote in a company name would close the string literal it is emitted into. The
    /// desktop caption goes into SOURCE, so it needs C# escaping and never XML escaping.</summary>
    [TestMethod]
    public void A_company_with_a_quote_is_escaped_for_source()
    {
        var branded = AppCustomization.Default with
        {
            Company = "The \"Big\" Shop",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        var source = SourceOf(branded, "LoginForm.cs");

        StringAssert.Contains(source, "\\\"Big\\\"");
        Assert.IsFalse(
            source.Contains("&quot;", StringComparison.Ordinal),
            "XML escaping inside a C# string literal would show up in the caption");
    }

    /// <summary>The accent the app was given tints the login here too, so the branding option does
    /// not stop at the shell.</summary>
    [TestMethod]
    public void The_accent_colours_the_brand_panel()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default with { Accent = "#0B6E4F" }, "LoginForm.cs"),
            "11, 110, 79");

    /// <summary>An unusable accent falls back rather than emitting something that will not
    /// compile.</summary>
    [TestMethod]
    public void A_malformed_accent_falls_back_to_the_default_fill()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default with { Accent = "mauve" }, "LoginForm.cs"),
            "Color.FromArgb(");

    [TestMethod]
    public void The_login_form_compiles_with_its_view_model()
        => EmittedDesktopSource.AssertCompilesTogether(
            [
                ("Views/LoginForm.cs", SourceOf(AppCustomization.Default, "LoginForm.cs")),
                ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Shop")),
                ("ViewModels/RelayCommand.cs", ViewModelTemplates.PortableRelayCommand("Shop")),
                ("ViewModels/LoginViewModel.cs", LoginViewModelTemplate.Source("Shop")),
                .. AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)),
                ("Session.cs", SessionTemplate.Source("Shop")),
                ("IStore.cs", StoreSeamTemplate.Source("Shop")),
                ("JsonStore.cs", JsonStoreTemplate.Source("Shop.Data")),
                ("StoreFactory.cs", StoreFactoryTemplate.Source("Shop", PersistenceKind.Json)),
            ]);

    /// <summary>The entry point compiles against both forms and the session it loops on.</summary>
    [TestMethod]
    public void The_entry_point_compiles()
        => EmittedDesktopSource.AssertCompilesTogether(
            [
                ("Program.cs", SourceOf(AppCustomization.Default, "Program.cs")),
                ("Views/LoginForm.cs",
                    "namespace Shop.Views;\n\npublic sealed class LoginForm : System.Windows.Forms.Form\n{\n}"),
                ("Views/MainForm.cs",
                    "namespace Shop.Views;\n\npublic sealed class MainForm : System.Windows.Forms.Form\n{\n}"),
                .. AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)),
                ("Session.cs", SessionTemplate.Source("Shop")),
                ("IStore.cs", StoreSeamTemplate.Source("Shop")),
                ("JsonStore.cs", JsonStoreTemplate.Source("Shop.Data")),
                ("StoreFactory.cs", StoreFactoryTemplate.Source("Shop", PersistenceKind.Json)),
            ]);
}
