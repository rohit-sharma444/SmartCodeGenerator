using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The company-derived title reaches every front end that has somewhere to show it.</summary>
[TestClass]
public class CustomizedTitleTests
{
    private static readonly AppCustomization Branded = AppCustomization.Default with
    {
        Company = "Codis Ltd",
        TitlePattern = AppCustomization.CompanyTitlePattern,
    };

    private static string SourceOf(ITargetScaffolder target, string name)
        => target.FrontEndFiles("Shop", ["Order"], Branded).Single(f => f.Name == name).Source;

    [TestMethod]
    public void The_wpf_window_title_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new WpfTarget(), "MainWindow.xaml"), "Title=\"Codis Ltd - Shop\"");

    [TestMethod]
    public void The_winforms_form_title_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new WinFormsTarget(), "MainForm.cs"), "Text = \"Codis Ltd - Shop\";");

    [TestMethod]
    public void The_console_banner_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new ConsoleTarget(), "Program.cs"), "Console.WriteLine(\"Codis Ltd - Shop\");");

    /// <summary>Under Default, TitleFor(appName) == appName, so an ungated banner would print a
    /// brand-new "Console.WriteLine("Shop");" that today's unbranded console app never had. The
    /// banner must be gated exactly like the Web API's root endpoint above.</summary>
    [TestMethod]
    public void An_unbranded_console_has_no_banner()
        => Assert.IsFalse(
            new ConsoleTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "Program.cs").Source
                .Contains("Console.WriteLine(\"Shop\");", StringComparison.Ordinal));

    /// <summary>The generated Web API has no OpenAPI document to title, so the title is served
    /// from the root instead. Emitted only when it differs from the bare project name.</summary>
    [TestMethod]
    public void The_web_api_serves_its_title_from_the_root()
        => StringAssert.Contains(
            SourceOf(new WebApiTarget(), "Program.cs"),
            "app.MapGet(\"/\", () => \"Codis Ltd - Shop\");");

    [TestMethod]
    public void An_unbranded_web_api_has_no_root_endpoint()
        => Assert.IsFalse(
            new WebApiTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "Program.cs").Source
                .Contains("app.MapGet(\"/\"", StringComparison.Ordinal));

    /// <summary>A quote in a title must not break out of the emitted string literal. Asserting
    /// only the absence of the corrupt form would also pass an escaper that simply deletes
    /// quotes, so the correctly-escaped form must be asserted present too.</summary>
    [TestMethod]
    public void A_quote_in_a_title_does_not_break_the_emitted_code()
    {
        var quoted = AppCustomization.Default with { Company = "Bob\"s", TitlePattern = "{company}" };

        var source = new WebApiTarget()
            .FrontEndFiles("Shop", ["Order"], quoted)
            .Single(f => f.Name == "Program.cs").Source;

        Assert.IsFalse(source.Contains("\"Bob\"s\"", StringComparison.Ordinal));
        StringAssert.Contains(source, "app.MapGet(\"/\", () => \"Bob\\\"s\");");
    }

    /// <summary>A raw newline inside a title must not land inside the C# string literal it is
    /// emitted into -- a literal source-level newline in a plain string is CS1010, "Newline in
    /// constant". Company is reachable with a real newline: CustomizationDefaults.Load
    /// deserializes it straight out of .codegen/app-profile.json with no sanitisation, and JSON
    /// can legally encode "Company": "Bob\nSmith".</summary>
    [TestMethod]
    public void A_newline_in_a_title_does_not_break_the_emitted_code()
    {
        var newlined = AppCustomization.Default with { Company = "Bob\nSmith", TitlePattern = "{company}" };

        var source = new ConsoleTarget()
            .FrontEndFiles("Shop", ["Order"], newlined)
            .Single(f => f.Name == "Program.cs").Source;

        Assert.IsFalse(
            source.Contains("Console.WriteLine(\"Bob\nSmith\");", StringComparison.Ordinal));
        StringAssert.Contains(source, "Console.WriteLine(\"Bob\\nSmith\");");
    }

    /// <summary>The brief calls for a Roslyn compile check here, mirroring the branded Web API's
    /// Program.cs. <see cref="WebApiTargetTests"/> has no compile test and no reusable stub set
    /// for it — this test project targets plain Microsoft.NET.Sdk, not Sdk.Web, so
    /// <c>WebApplication</c>/<c>MapGet</c> do not resolve against this process's trusted platform
    /// assemblies, and building an ASP.NET Core stub set from scratch would be exactly the
    /// elaborate harness the task brief says not to invent. Asserting on the emitted text and its
    /// ordering is the fallback the brief calls for; Task 14's end-to-end generated-app build is
    /// the real compile proof for this front end.</summary>
    [TestMethod]
    public void A_branded_web_api_program_emits_the_root_endpoint_before_app_Run()
    {
        var source = new WebApiTarget()
            .FrontEndFiles("Acme", ["Order"], Branded)
            .Single(f => f.Name == "Program.cs").Source;

        Assert.IsLessThan(
            source.IndexOf("app.Run();", StringComparison.Ordinal),
            source.IndexOf("app.MapGet(\"/\", () => \"Codis Ltd - Acme\");", StringComparison.Ordinal));
    }
}
