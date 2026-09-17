using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The login must be the first window, and signing out must return to it without
/// restarting the process.</summary>
[TestClass]
public class WpfLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new WpfTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_wpf_app_emits_a_login_window()
    {
        var names = Files(AppCustomization.Default).Select(f => f.Name).ToList();

        CollectionAssert.Contains(names, "LoginWindow.xaml");
        CollectionAssert.Contains(names, "LoginWindow.xaml.cs");
        CollectionAssert.Contains(names, "App.xaml.cs");
    }

    [TestMethod]
    public void Nologin_emits_none_of_them()
    {
        var names = Files(AppCustomization.Default with { Login = false })
            .Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "LoginWindow.xaml");
        CollectionAssert.DoesNotContain(names, "LoginWindow.xaml.cs");
        CollectionAssert.DoesNotContain(names, "App.xaml.cs");
    }

    /// <summary>The whole point: the shell must not be constructed until a sign-in succeeded.</summary>
    [TestMethod]
    public void The_entry_point_shows_the_login_before_the_shell()
    {
        var app = SourceOf(AppCustomization.Default, "App.xaml.cs");

        var login = app.IndexOf("new LoginWindow()", StringComparison.Ordinal);
        var shell = app.IndexOf("new MainWindow()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, login, "the login window is never constructed");
        Assert.IsGreaterThan(-1, shell, "the shell is never constructed");
        Assert.IsLessThan(shell, login, "the shell must not be built before the sign-in");
    }

    /// <summary>Sign-out loops back to the login. Restarting the process instead would lose the
    /// window position and flash the taskbar.</summary>
    [TestMethod]
    public void Signing_out_returns_to_the_login()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "App.xaml.cs"), "Session.SignOutRequested");

    /// <summary>
    /// With no StartupUri and no window yet, the default OnLastWindowClose shutdown mode would end
    /// the application the moment the login window closed — before the shell could open.
    /// </summary>
    [TestMethod]
    public void The_entry_point_sets_an_explicit_shutdown_mode()
    {
        var app = SourceOf(AppCustomization.Default, "App.xaml.cs");

        StringAssert.Contains(app, "ShutdownMode.OnExplicitShutdown");
        StringAssert.Contains(app, "Shutdown();");
    }

    /// <summary>Accounts must exist before the login screen asks for one.</summary>
    [TestMethod]
    public void The_entry_point_seeds_the_administrator_before_asking()
    {
        var app = SourceOf(AppCustomization.Default, "App.xaml.cs");

        Assert.IsLessThan(
            app.IndexOf("new LoginWindow()", StringComparison.Ordinal),
            app.IndexOf("UserSeed.EnsureAdminSeeded();", StringComparison.Ordinal));
    }

    /// <summary>The password must never be a bound string on the view model: WPF's PasswordBox
    /// deliberately does not expose Password as a bindable property, and a TextBox would show the
    /// password on screen.</summary>
    [TestMethod]
    public void The_password_is_entered_through_a_password_box()
    {
        var xaml = SourceOf(AppCustomization.Default, "LoginWindow.xaml");

        StringAssert.Contains(xaml, "PasswordBox");
        Assert.IsFalse(
            xaml.Contains("Text=\"{Binding Password", StringComparison.Ordinal),
            "binding the password would hold it in a bindable string");
    }

    /// <summary>Enter is how anyone signs in. A login screen that needs the mouse is wrong.</summary>
    [TestMethod]
    public void Enter_signs_in()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "LoginWindow.xaml"), "IsDefault=\"True\"");

    [TestMethod]
    public void The_brand_panel_shows_the_company_when_there_is_one()
    {
        var branded = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        StringAssert.Contains(SourceOf(branded, "LoginWindow.xaml"), "Codis Ltd");
    }

    /// <summary>An unbranded app still has a name worth showing; a blank panel reads as a
    /// rendering failure.</summary>
    [TestMethod]
    public void The_brand_panel_falls_back_to_the_app_name()
        => StringAssert.Contains(SourceOf(AppCustomization.Default, "LoginWindow.xaml"), "Shop");

    /// <summary>An ampersand in a company name is XML-escaped, or the XAML does not parse.</summary>
    [TestMethod]
    public void A_company_with_an_ampersand_is_escaped()
    {
        var branded = AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        var xaml = SourceOf(branded, "LoginWindow.xaml");

        StringAssert.Contains(xaml, "&amp;");
        Assert.IsFalse(
            xaml.Contains("Smith & Sons", StringComparison.Ordinal),
            "a raw ampersand makes the XAML unparseable");
    }

    /// <summary>The accent the app was given tints the login, or the branding option stops at the
    /// shell and the first screen anyone sees is generic.</summary>
    [TestMethod]
    public void The_accent_colours_the_brand_panel()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default with { Accent = "#0B6E4F" }, "LoginWindow.xaml"),
            "#0B6E4F");

    /// <summary>What the XAML compiler generates for LoginWindow, which Roslyn alone cannot: the
    /// named controls and InitializeComponent. Same stand-in the layout tests use.</summary>
    private const string LoginWindowXamlStub = """
        namespace Shop.Views;

        public partial class LoginWindow
        {
            internal System.Windows.Controls.TextBox UserNameBox = new();
            internal System.Windows.Controls.PasswordBox PasswordBox = new();

            private void InitializeComponent() { }
        }
        """;

    /// <summary>The shell, which App.xaml.cs opens after a successful sign-in.</summary>
    private const string MainWindowStub = """
        namespace Shop.Views;

        public partial class MainWindow : System.Windows.Window
        {
        }
        """;

    /// <summary>The real auth core and data layer the login view model actually calls.</summary>
    private static (string Path, string Code)[] AuthAndData()
        =>
        [
            .. AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)),
            ("Session.cs", SessionTemplate.Source("Shop")),
            ("IStore.cs", StoreSeamTemplate.Source("Shop")),
            ("JsonStore.cs", JsonStoreTemplate.Source("Shop.Data")),
            ("StoreFactory.cs", StoreFactoryTemplate.Source("Shop", PersistenceKind.Json)),
            // JsonStore now asks Permissions before every write (Task 7's coarse store guard).
            // ValidationContext.cs in turn names IEntityLookup, which Permissions does not use
            // directly but still has to resolve for the file to compile.
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Shop")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Shop")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Shop", Array.Empty<string>(), login: true)),
            // JsonStore also asks ActingUser.Role() unconditionally now. Real Session/auth-core
            // files are already in this fixture, so sessionAuth: true is the faithful choice.
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Shop", sessionAuth: true)),
        ];

    /// <summary>
    /// The login window's code-behind compiles against the real view model and the real auth core.
    /// </summary>
    /// <remarks>
    /// Emitted WPF compiles only against the WindowsDesktop reference pack, which is why this goes
    /// through <see cref="EmittedDesktopSource"/>. The whole emitted app is NOT compiled here — the
    /// shell's own code-behind has its own stubs and its own test — only what the login touches.
    /// </remarks>
    [TestMethod]
    public void The_login_window_compiles_with_its_view_model()
        => EmittedDesktopSource.AssertCompilesTogether(
            [
                ("Views/LoginWindow.xaml.cs", WpfLoginTemplate.CodeBehind("Shop")),
                ("Views/LoginWindow.g.cs", LoginWindowXamlStub),
                ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Shop")),
                ("ViewModels/RelayCommand.cs", ViewModelTemplates.RelayCommand("Shop")),
                ("ViewModels/LoginViewModel.cs", LoginViewModelTemplate.Source("Shop")),
                .. AuthAndData(),
            ]);

    /// <summary>The entry point compiles against both windows and the session it loops on.</summary>
    [TestMethod]
    public void The_entry_point_compiles()
        => EmittedDesktopSource.AssertCompilesTogether(
            [
                ("App.xaml.cs", WpfLoginTemplate.AppCodeBehind("Shop")),
                ("Views/LoginWindow.cs",
                    "namespace Shop.Views;\n\npublic partial class LoginWindow : System.Windows.Window\n{\n}"),
                ("Views/MainWindow.cs", MainWindowStub),
                .. AuthAndData(),
            ]);
}
