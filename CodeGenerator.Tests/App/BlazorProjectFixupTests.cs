using System.Collections.Generic;
using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The one line of the Blazor template's <c>Program.cs</c> a namespace prefix invalidates.
/// </summary>
/// <remarks>
/// Found by <c>GeneratedProjectBuildTests.PrefixedProject_BuildsAndItsTestsPass("blazor")</c>, which
/// is the only honest proof — but that row takes the best part of a minute, so the behaviour is
/// pinned down here too.
/// </remarks>
[TestClass]
public class BlazorProjectFixupTests
{
    /// <summary>The head of what `dotnet new blazor -n Shop` writes, verbatim.</summary>
    private const string TemplateProgram = """
using Shop.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
""";

    /// <summary>
    /// What `dotnet new blazor` writes on .NET 10, captured from a real generation rather than
    /// remembered — the abbreviated <see cref="TemplateProgram"/> above has neither
    /// UseAntiforgery nor MapStaticAssets, and both matter to the authentication surgery.
    /// </summary>
    private const string RealTemplateProgram = """
using Shop.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
""";

    private readonly List<string> _tempFolders = new();

    private string NewProjectFolder(string program = TemplateProgram)
    {
        var folder = Path.Combine(Path.GetTempPath(), "bzfixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        _tempFolders.Add(folder);

        File.WriteAllText(Path.Combine(folder, "Program.cs"), program);

        return folder;
    }

    private static string ProgramIn(string folder)
        => File.ReadAllText(Path.Combine(folder, "Program.cs"));

    private string Fixed(string program)
    {
        var folder = NewProjectFolder(program);

        BlazorProjectFixup.EnableAuthentication(folder, "Shop");

        return File.ReadAllText(Path.Combine(folder, "Program.cs"));
    }

    [TestMethod]
    public void EnableAuthentication_RegistersTheServicesBeforeTheAppIsBuilt()
    {
        var program = Fixed(RealTemplateProgram);

        Assert.IsLessThan(
            program.IndexOf("var app = builder.Build();", StringComparison.Ordinal),
            program.IndexOf("AddAuthentication", StringComparison.Ordinal));

        StringAssert.Contains(program, "AddCascadingAuthenticationState");
        StringAssert.Contains(program, "using Microsoft.AspNetCore.Authentication.Cookies;");
    }

    /// <summary>
    /// The order ASP.NET Core requires: authentication and authorization run BEFORE antiforgery.
    /// </summary>
    /// <remarks>
    /// Wrong order is a runtime failure with a misleading message, and no unit test over emitted
    /// strings would see it — only the order of the inserted lines can be checked here.
    /// </remarks>
    [TestMethod]
    public void EnableAuthentication_PutsTheMiddlewareBeforeAntiforgery()
    {
        var program = Fixed(RealTemplateProgram);

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        var antiforgery = program.IndexOf("app.UseAntiforgery();", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, authentication, "authentication middleware was never added");
        Assert.IsLessThan(authorization, authentication);
        Assert.IsLessThan(antiforgery, authorization);
    }

    /// <summary>A Program.cs with no UseAntiforgery still has to get the middleware, and before
    /// the endpoints rather than nowhere at all.</summary>
    [TestMethod]
    public void EnableAuthentication_FallsBackToTheEndpointsWhenThereIsNoAntiforgery()
    {
        var program = Fixed(TemplateProgram);

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, authentication, "authentication middleware was never added");
        Assert.IsLessThan(
            program.IndexOf("app.MapRazorComponents<App>()", StringComparison.Ordinal),
            authentication);
    }

    /// <summary>Moving or dropping MapStaticAssets breaks the static web asset manifest, and the
    /// app serves no stylesheets at all.</summary>
    [TestMethod]
    public void EnableAuthentication_LeavesTheStaticAssetMappingAlone()
        => StringAssert.Contains(Fixed(RealTemplateProgram), "app.MapStaticAssets();");

    [TestMethod]
    public void EnableAuthentication_MapsTheAuthEndpoints()
        => StringAssert.Contains(Fixed(RealTemplateProgram), "app.MapAuthEndpoints();");

    /// <summary>
    /// MapAuthEndpoints is an EXTENSION method, so Program.cs needs the namespace it lives in.
    /// </summary>
    /// <remarks>
    /// Without it the build fails with CS1061 -- "WebApplication does not contain a definition
    /// for MapAuthEndpoints" -- which reads as a missing package rather than a missing using.
    /// Found by building a generated app.
    /// </remarks>
    [TestMethod]
    public void EnableAuthentication_ImportsTheNamespaceTheEndpointsLiveIn()
        => StringAssert.Contains(Fixed(RealTemplateProgram), "using Shop.Auth;");

    [TestMethod]
    public void EnableAuthentication_IsIdempotent()
    {
        var once = Fixed(RealTemplateProgram);

        Assert.AreEqual(once, Fixed(once));
    }

    /// <summary>A Program.cs this rewrite does not recognise is left alone, like every other
    /// fixup here.</summary>
    [TestMethod]
    public void EnableAuthentication_LeavesAnUnrecognisedProgramAlone()
    {
        const string other = "Console.WriteLine(\"not a blazor host\");";

        Assert.AreEqual(other, Fixed(other));
    }

    [TestCleanup]
    public void CleanupTempFolders()
    {
        foreach (var folder in _tempFolders)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void ReconcileNamespace_RepointsTheComponentsImportAtThePrefixedNamespace()
    {
        var folder = NewProjectFolder();

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        // Without this the project fails with CS0246 on App in MapRazorComponents<App>(): the
        // prefix moved App.razor to CodisLtd.Shop.Components and the import still names Shop.
        Assert.Contains("using CodisLtd.Shop.Components;", ProgramIn(folder));
        Assert.DoesNotContain("using Shop.Components;", ProgramIn(folder));
    }

    [TestMethod]
    public void ReconcileNamespace_LeavesEverythingElseInProgramAlone()
    {
        var folder = NewProjectFolder();

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        var program = ProgramIn(folder);

        Assert.Contains("app.MapRazorComponents<App>()", program);
        Assert.Contains(".AddInteractiveServerRenderMode();", program);
        Assert.Contains("app.Run();", program);
    }

    [TestMethod]
    public void ReconcileNamespace_WithNoPrefix_ChangesNothing()
    {
        var folder = NewProjectFolder();

        BlazorProjectFixup.ReconcileNamespace(folder, "Shop");

        Assert.AreEqual(TemplateProgram, ProgramIn(folder));
    }

    [TestMethod]
    public void ReconcileNamespace_RunsOnEveryScaffoldSoItWorksFromWhateverIsThere()
    {
        // The prefix can change between runs, so the rewrite cannot rely on the file still saying
        // what the template first wrote -- it matches the shape of the import that is there now.
        var folder = NewProjectFolder();

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");
        BlazorProjectFixup.ReconcileNamespace(folder, "Acme.Shop");

        Assert.Contains("using Acme.Shop.Components;", ProgramIn(folder));
        Assert.DoesNotContain("CodisLtd", ProgramIn(folder));
    }

    [TestMethod]
    public void ReconcileNamespace_IsIdempotent()
    {
        var folder = NewProjectFolder();

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");
        var once = ProgramIn(folder);

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        Assert.AreEqual(once, ProgramIn(folder));
    }

    [TestMethod]
    public void ReconcileNamespace_LeavesAHandWrittenLibraryImportAlone()
    {
        // A user who has since edited their app may well import a component library. Only the
        // template's own import -- the first line, and only in a file that still maps App -- is
        // this method's business.
        var folder = NewProjectFolder(
            TemplateProgram.Replace(
                "using Shop.Components;",
                "using Shop.Components;\nusing Blazored.Toast.Components;"));

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        var program = ProgramIn(folder);

        Assert.Contains("using CodisLtd.Shop.Components;", program);
        Assert.Contains("using Blazored.Toast.Components;", program);
    }

    [TestMethod]
    public void ReconcileNamespace_LeavesAProgramItDoesNotRecogniseAlone()
    {
        // Not the Blazor template's Program.cs -- nothing here says the components import means
        // what this rewrite assumes, so it must not guess.
        const string unfamiliar = "using Shop.Components;\n\nConsole.WriteLine(\"hello\");\n";

        var folder = NewProjectFolder(unfamiliar);

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        Assert.AreEqual(unfamiliar, ProgramIn(folder));
    }

    [TestMethod]
    public void ReconcileNamespace_WithNoProgramAtAll_DoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bzfixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        _tempFolders.Add(folder);

        BlazorProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        Assert.IsFalse(File.Exists(Path.Combine(folder, "Program.cs")));
    }
}
