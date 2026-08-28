using Bunit;
using CodeGenerationEngine.Core;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page, as a page. What it decides is asserted in <see cref="ValidationAddTests"/> without a
/// renderer; what is here is only what needs one.
/// </summary>
/// <remarks>
/// The target arrives through the query string, and a <c>[SupplyParameterFromQuery]</c> parameter
/// cannot be handed to the renderer directly - bUnit refuses it outright, because in a real app
/// nothing but the URL can supply one. So every test here navigates first and renders second,
/// which is also the only shape that proves the two query names the tree will link with are the
/// names the page reads.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ValidationPageTests
{
    private string _root = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void MakeClass()
    {
        ValidationRegistry.Clear();

        _root = Path.Combine(Path.GetTempPath(), "vp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _file = Path.Combine(_root, "Employee.cs");
        File.WriteAllText(_file,
            "namespace Acme\r\n{\r\n    public class Employee\r\n    {\r\n" +
            "        public int Age { get; set; }\r\n    }\r\n}\r\n");
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        ValidationRegistry.Clear();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        var context = new BunitContext();

        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton(new ChatSession(
            new Workspace(_root, projectPathSet: false), AiLayer.Create(), []));

        return context;
    }

    /// <summary>Renders the page as the tree's link would reach it.</summary>
    private static IRenderedComponent<ValidationPage> Open(
        BunitContext context, string className, string file)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"/chat/validation?t=token&class={Uri.EscapeDataString(className)}"
            + $"&file={Uri.EscapeDataString(file)}");

        return context.Render<ValidationPage>();
    }

    [TestMethod]
    public void Lists_the_properties_of_the_class()
    {
        using var context = NewContext();

        var page = Open(context, "Employee", _file);

        StringAssert.Contains(page.Find(".val-props").TextContent, "Age");
    }

    [TestMethod]
    public void Says_so_when_the_file_is_outside_the_workspace()
    {
        using var context = NewContext();

        var page = Open(context, "Elsewhere", Path.Combine(Path.GetTempPath(), "Elsewhere.cs"));

        StringAssert.Contains(page.Markup, "outside the loaded workspace");
    }

    [TestMethod]
    public void Names_the_sentence_it_could_not_understand()
    {
        using var context = NewContext();

        var page = Open(context, "Employee", _file);

        // The box binds on oninput, so Change() fires an event it has no handler for.
        page.Find("textarea").Input("Age ought to be sensible");
        page.Find("button.val-add").Click();

        StringAssert.Contains(page.Find(".val-refused").TextContent, "Age ought to be sensible");
        StringAssert.Contains(page.Markup, "Nothing was written");
    }

    [TestMethod]
    public void Lists_the_rule_after_it_is_added()
    {
        using var context = NewContext();

        var page = Open(context, "Employee", _file);

        // The box binds on oninput, so Change() fires an event it has no handler for.
        page.Find("textarea").Input("Age must be at least 18");
        page.Find("button.val-add").Click();

        // The list of what is on the class, not the "added" confirmation: this is the assertion
        // that the page re-read the file rather than just echoing what it was told.
        StringAssert.Contains(page.Find(".val-existing").TextContent, "Age must be at least 18");
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }

    /// <summary>
    /// The link back to the chat keeps the session token.
    /// </summary>
    /// <remarks>
    /// The host mints a token per run and 404s any request to /chat without it, so a bare "/chat"
    /// href is a link to a 404 - and the way back is the one part of this page a person is certain
    /// to use.
    /// </remarks>
    [TestMethod]
    public void The_way_back_to_the_chat_carries_the_token()
    {
        using var context = NewContext();

        var page = Open(context, "Employee", _file);

        StringAssert.Contains(page.Find("a.val-back").GetAttribute("href"), "t=token");
    }
}
