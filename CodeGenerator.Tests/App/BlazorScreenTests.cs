using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;
using CodeGenerator.Tests.Helper;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The business screens on the web: which screen a page renders, which entities get one at all, and
/// the reflection all three screens share.
/// </summary>
/// <remarks>
/// Razor cannot be compiled here, so the markup is asserted as text and proved for real by
/// <c>GeneratedProjectBuildTests.BusinessProject_Builds</c>, which generates a web app and builds
/// it. <c>ScreenFields</c> is plain C#, so it is compiled AND run — that is where the logic the
/// markup depends on actually lives.
/// </remarks>
[TestClass]
public class BlazorScreenTests
{
    /// <summary>Order is a transaction (a foreign key, a date and an amount), OrderLine its lines,
    /// Customer reference data.</summary>
    private static readonly string[] Names = ["Customer", "Order", "OrderLine"];

    /// <summary>Shaped by the fixture the heuristic's own tests use, audit fields and all: a
    /// foreign key is a &lt;Target&gt;Id int AND a navigation property, not just a name.</summary>
    private static EntityRoleMap Roles()
        => EntityRoles.Classify(
            [
                EntityRolesTests.Entity("Customer", ("Name", "string")),
                EntityRolesTests.Entity(
                    "Order",
                    [.. EntityRolesTests.ForeignKeyTo("Customer"),
                     ("OrderedOn", "DateTime"), ("Amount", "decimal")]),
                EntityRolesTests.Entity(
                    "OrderLine", [.. EntityRolesTests.ForeignKeyTo("Order"), ("Quantity", "int")]),
            ]);

    private static IReadOnlyList<GeneratedFile> Files(AppCustomization? customization = null)
        => new BlazorTarget().FrontEndFiles(
            new FrontEndContext("Shop", Names, customization ?? AppCustomization.Default, Roles()));

    private static string Source(string name, AppCustomization? customization = null)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void The_classification_this_test_relies_on_is_what_the_classifier_says()
    {
        var roles = Roles();

        Assert.AreEqual(EntityRole.Transaction, roles.RoleOf("Order"));
        Assert.AreEqual(EntityRole.DocumentLine, roles.RoleOf("OrderLine"));
        Assert.AreEqual(EntityRole.Master, roles.RoleOf("Customer"));
    }

    [TestMethod]
    public void A_master_page_renders_the_master_screen()
        => Assert.Contains("<MasterScreen TRecord=\"Customer\"", Source("CustomerPage.razor"));

    [TestMethod]
    public void A_transaction_page_renders_the_transaction_screen()
    {
        var page = Source("OrderPage.razor");

        Assert.Contains("<TransactionScreen TRecord=\"Order\"", page);
        Assert.DoesNotContain("<MasterScreen", page);
    }

    /// <summary>The whole point of the third role: a line is edited inside its parent, so it has no
    /// screen, no route and no place in the rail.</summary>
    [TestMethod]
    public void A_document_line_gets_no_page_no_link_and_no_tile()
    {
        var files = Files();

        Assert.IsFalse(files.Any(f => f.Name.StartsWith("OrderLine", StringComparison.Ordinal)));

        foreach (var name in new[] { "NavMenu.razor", "Home.razor" })
            Assert.DoesNotContain("OrderLine", Source(name));
    }

    [TestMethod]
    public void Every_entity_gets_an_editor_route()
    {
        var editor = Source("OrderEditorPage.razor");

        Assert.Contains("@page \"/order/{Id:int}\"", editor);
        Assert.Contains("@page \"/order/new\"", editor);

        // Null on the "new" route, which is how the editor tells the two apart.
        Assert.Contains("[Parameter] public int? Id { get; set; }", editor);
        Assert.Contains("<RecordEditor TRecord=\"Order\" Id=\"@Id\"", editor);
    }

    /// <summary>
    /// The web's answer to the desktop message box.
    /// </summary>
    /// <remarks>
    /// This is what a modal was buying: an editor that fills the content area can be navigated away
    /// from, so something has to ask before the edits are lost. Without the lock, a rail click
    /// silently discards a half-typed record.
    /// </remarks>
    [TestMethod]
    public void The_editor_guards_navigation_away_from_unsaved_changes()
    {
        var editor = Source("RecordEditor.razor");

        Assert.Contains("<NavigationLock", editor);
        Assert.Contains("OnBeforeInternalNavigation=\"OnBeforeNavigation\"", editor);
        Assert.Contains("\"confirm\", \"Discard your changes?\"", editor);
        Assert.Contains("context.PreventNavigation()", editor);

        // Save and Cancel are deliberate departures and must not be interrogated.
        Assert.Contains("if (_leaving || !IsDirty)", editor);
    }

    /// <summary>The editor edits a copy, which is the reason Cancel can mean something.</summary>
    [TestMethod]
    public void The_editor_works_on_a_copy()
    {
        var editor = Source("RecordEditor.razor");

        Assert.Contains("ScreenFields.Clone(stored)", editor);
        Assert.Contains("_openedAs = ScreenFields.Snapshot(_draft)", editor);
    }

    /// <summary>
    /// The editor runs the generated validators, through the pipeline.
    /// </summary>
    /// <remarks>
    /// It writes through the store rather than through Repository&lt;T&gt;, so nothing else on this
    /// path would run them — and the web would then accept records the API rejects. A context
    /// rather than a bare record: <c>Validate(T item)</c> no longer exists, and the operation and
    /// the acting user are what a Security stage reads.
    /// </remarks>
    [TestMethod]
    public void The_editor_validates_when_the_app_has_validators()
    {
        var editor = Source("RecordEditor.razor");

        Assert.Contains("ValidatorFactory.Create<TRecord>().Validate(context)", editor);
        // Built with the full constructor, not ValidationContext<TRecord>.For -- that factory's
        // own doc comment says it is for tests and callers with no real acting user, and this
        // editor has one when the app has a login (AppCustomization.Default.Login is true here).
        Assert.Contains("new ValidationContext<TRecord>(", editor);
        Assert.Contains("state?.User.FindFirst(ClaimTypes.Role)?.Value", editor);

        // With validation off there is no pipeline to run -- ValidatorFactory names a class that
        // does not exist under novalidation. But the app still has a login here
        // (AppCustomization.Default.Login is true), and Save must still ask Permissions itself
        // with the real signed-in role before writing -- the same explicit guard Delete already
        // has, independent of validation -- or a novalidation Blazor app with a login would have
        // NO permission check on save at all. That guard is what still needs Shop.Validation
        // imported (Permissions/ValidationOperation live there), even with no pipeline to call.
        var bare = Source(
            "RecordEditor.razor", AppCustomization.Default with { Validation = false });

        Assert.DoesNotContain("ValidatorFactory", bare);
        Assert.Contains("Shop.Validation", bare);
        Assert.Contains("Permissions.Allows(ValidationOperation.Update", bare);
    }

    /// <summary>
    /// A transaction's editor holds its lines; a master's holds none.
    /// </summary>
    /// <remarks>
    /// The page supplies them, because it is the only place that knows both the record type and the
    /// line type — the component is generic over the record alone.
    /// </remarks>
    [TestMethod]
    public void The_editor_page_hands_the_editor_its_lines()
    {
        var editor = Source("OrderEditorPage.razor");

        Assert.Contains("Lines=\"@(() => new ILineSet[] { new LineSet<OrderLine>(\"OrderLine\", \"OrderId\") })\"", editor);
        Assert.Contains("@using Shop.Screens", editor);

        // Reference data has no lines, and the attribute is absent rather than empty.
        Assert.DoesNotContain("Lines=", Source("CustomerEditorPage.razor"));
    }

    [TestMethod]
    public void The_editor_renders_a_table_per_set_of_lines()
    {
        var editor = Source("RecordEditor.razor");

        Assert.Contains("@foreach (var lines in _lines)", editor);
        Assert.Contains("lines.Add()", editor);
        Assert.Contains("lines.Remove(line)", editor);

        // The parent's id is stamped on save, so it is not a column anyone types into.
        Assert.Contains("field.Name, lines.ForeignKey", editor);

        // And they go after the record, which is where the id comes from.
        Assert.Contains("lines.Save(ScreenFields.IdOf(_draft))", editor);
    }

    /// <summary>A line edit alone has to make the editor dirty, or the guard lets it go.</summary>
    [TestMethod]
    public void A_line_edit_counts_as_an_unsaved_change()
        => Assert.Contains("_lines.Any(lines => lines.IsDirty)", Source("RecordEditor.razor"));

    /// <summary>The same file the desktop targets emit — one set of semantics, three views.</summary>
    [TestMethod]
    public void The_web_emits_the_same_line_set_the_desktop_does()
    {
        var emitted = Files().Single(f => f.Name == "LineSet.cs");

        Assert.AreEqual("Screens", emitted.RelativeFolder);
        Assert.AreEqual(AppScaffolding.Screens.LineSetTemplate.Source("Shop"), emitted.Source);
    }

    [TestMethod]
    public void The_transaction_screen_has_the_date_range_and_the_totals()
    {
        var screen = Source("TransactionScreen.razor");

        Assert.Contains("ScreenFields.DateField(typeof(TRecord))", screen);
        Assert.Contains("ScreenFields.InRange(record, DateField, _from, _to)", screen);
        Assert.Contains("ScreenFields.MoneyColumns(typeof(TRecord))", screen);
        Assert.Contains("<tfoot>", screen);
    }

    /// <summary>The list finds; the editor changes. A typeable table would put the application back
    /// to editing two different ways.</summary>
    [TestMethod]
    public void Neither_list_can_be_typed_into()
    {
        foreach (var screen in new[] { "MasterScreen.razor", "TransactionScreen.razor" })
        {
            Assert.DoesNotContain("<input value=", Source(screen));
            Assert.Contains("Navigation.NavigateTo($\"/{Route}/{ScreenFields.IdOf(record)}\")", Source(screen));
        }
    }

    [TestMethod]
    public void The_rail_is_grouped_by_role()
    {
        var nav = Source("NavMenu.razor");

        Assert.Contains(">Masters</div>", nav);
        Assert.Contains(">Transactions</div>", nav);
        Assert.Contains("href=\"customer\"", nav);
        Assert.Contains("href=\"order\"", nav);
    }

    /// <summary>A heading with nothing under it is noise.</summary>
    [TestMethod]
    public void A_group_with_no_entities_has_no_heading()
    {
        var mastersOnly = new BlazorTarget().FrontEndFiles(
            new FrontEndContext(
                "Shop", ["Customer"], AppCustomization.Default, EntityRoleMap.Empty));

        var nav = mastersOnly.Single(f => f.Name == "NavMenu.razor").Source;

        Assert.Contains(">Masters</div>", nav);
        Assert.DoesNotContain(">Transactions</div>", nav);
    }

    [TestMethod]
    public void The_home_page_is_a_dashboard_of_counts_grouped_the_same_way()
    {
        var home = Source("Home.razor");

        Assert.Contains("@page \"/\"", home);
        Assert.Contains("new(\"Transactions\", [new(\"Order\", \"order\")])", home);
        Assert.Contains("new(\"Masters\", [new(\"Customer\", \"customer\")])", home);
        Assert.Contains("_counts[\"Order\"] = StoreFactory.Create<Order>().GetAll().Count;", home);

        // Transactions first: on a business screen they are the work.
        Assert.IsLessThan(home.IndexOf("Masters", StringComparison.Ordinal), home.IndexOf("Transactions", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every page refuses an unauthenticated visitor.
    /// </summary>
    /// <remarks>
    /// Branch 1 shipped a home page with no attribute on it, which listed the whole model to anyone
    /// who asked for "/". A new page is exactly where that recurs, and this branch adds one per
    /// entity.
    /// </remarks>
    [TestMethod]
    public void Every_page_is_authorized()
    {
        foreach (var page in Pages())
        {
            Assert.Contains("@using Microsoft.AspNetCore.Authorization", page.Source, page.Name);

            // Not "[Authorize]" exactly: the Users page carries [Authorize(Roles = "Admin")].
            Assert.Contains("@attribute [Authorize", page.Source, page.Name);
        }
    }

    /// <summary>
    /// Every page except the login form.
    /// </summary>
    /// <remarks>
    /// Login is deliberately neither authorized nor interactive: it is static-rendered and posts to
    /// a minimal-API endpoint, because an interactive circuit cannot set a cookie.
    /// </remarks>
    private static IEnumerable<GeneratedFile> Pages()
        => Files().Where(f => f.RelativeFolder == "Components/Pages" && f.Name != "Login.razor");

    [TestMethod]
    public void An_app_with_no_login_authorizes_nothing_and_still_deletes()
    {
        var open = AppCustomization.Default with { Login = false };

        foreach (var page in Files(open).Where(f => f.RelativeFolder == "Components/Pages"))
            Assert.DoesNotContain("[Authorize", page.Source, page.Name);

        var list = Source("MasterScreen.razor", open);

        Assert.DoesNotContain("AuthorizeView", list);
        Assert.Contains(">Delete</button>", list);
    }

    /// <summary>
    /// Delete is offered to exactly the roles its handler will accept.
    /// </summary>
    /// <remarks>
    /// The button used to be wrapped in <c>AuthorizeView Roles="Admin"</c> while the handler asked
    /// the permission matrix, so the two disagreed the moment the matrix did — and this button is
    /// shared by both grids, so a plain user was hidden a delete on a TRANSACTION screen that the
    /// matrix permits and that the same user gets on WinForms and WPF. Hidden-but-permitted is the
    /// mirror image of the hardcoded check the matrix replaced.
    /// <para>
    /// Hidden is still not the same as refused: the handler asks again, because a button that can
    /// be reached another way must still be refused. The point is that both ask the SAME call.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void The_delete_button_and_its_handler_ask_one_matrix_call()
    {
        // Novalidation: with the pipeline (the default), the handler's OWN check moves onto
        // ValidatorFactory instead of calling MayDelete literally — see
        // DeleteGoesThroughThePipelineTests, which proves the pipeline half. Here, with no
        // pipeline generated at all, the handler still asks MayDelete directly, and this is
        // the invariant that must keep holding: the button and the handler share ONE call.
        var novalidation = AppCustomization.Default with { Validation = false };

        foreach (var screen in new[] { "MasterScreen.razor", "TransactionScreen.razor" })
        {
            var list = Source(screen, novalidation);

            Assert.Contains("<AuthorizeView>", list, screen);
            Assert.Contains("@if (MayDelete(context))", list, screen);
            Assert.Contains("if (!MayDelete(state))", list, screen);
            Assert.Contains(
                "private static bool MayDelete(AuthenticationState? state)", list, screen);

            // One decision, asked twice. Two calls would be two places to change, which is what
            // let the button and the handler drift in the first place.
            Assert.AreEqual(
                1,
                EmittedLines.Occurrences(list, "Permissions.Allows("),
                $"{screen} asks the matrix more than once");

            Assert.DoesNotContain("Roles=\"Admin\"", list, screen);
        }
    }

    /// <summary>
    /// AuthenticationState is in Microsoft.AspNetCore.Components.Authorization, a DIFFERENT
    /// namespace from the Microsoft.AspNetCore.Authorization that [Authorize] needs. Getting this
    /// wrong cost an afternoon on branch 1.
    /// </summary>
    [TestMethod]
    public void The_screens_import_the_authorization_namespace_components_use()
        => Assert.Contains(
            "@using Microsoft.AspNetCore.Components.Authorization", Source("MasterScreen.razor"));

    /// <summary>
    /// The rail is not shown to a visitor who has not signed in.
    /// </summary>
    /// <remarks>
    /// The layout renders on the login page too, so an unauthenticated visitor was handed the name
    /// of every entity in the model before signing in — the same leak the home page had, seen on a
    /// running app rather than in an assertion.
    /// </remarks>
    [TestMethod]
    public void The_rail_names_no_entity_to_a_visitor_who_has_not_signed_in()
    {
        var nav = Source("NavMenu.razor");

        var gate = nav.IndexOf("<AuthorizeView>", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, gate, "the entity groups are not behind an AuthorizeView");
        Assert.IsGreaterThan(gate, nav.IndexOf("href=\"customer\"", StringComparison.Ordinal));
        Assert.IsGreaterThan(gate, nav.IndexOf("href=\"order\"", StringComparison.Ordinal));
    }

    /// <summary>An app with no accounts has nobody to authorize, so the rail is just a rail.</summary>
    [TestMethod]
    public void An_app_with_no_login_has_no_gate_on_its_rail()
    {
        var nav = Source("NavMenu.razor", AppCustomization.Default with { Login = false });

        Assert.DoesNotContain("AuthorizeView", nav);
        Assert.Contains("href=\"customer\"", nav);
    }

    /// <summary>
    /// The login page opts out of interactive routing.
    /// </summary>
    /// <remarks>
    /// It needs an HttpContext for its antiforgery token and its cookie, and an interactive circuit
    /// has neither. The other half of the opt-out is in BlazorProjectFixup.EnableInteractiveRouting;
    /// this attribute alone would leave the page rendering Not Found once the circuit started.
    /// </remarks>
    [TestMethod]
    public void The_login_page_stays_static_under_an_interactive_router()
    {
        var login = Source("Login.razor");

        Assert.Contains("@attribute [ExcludeFromInteractiveRouting]", login);
        Assert.DoesNotContain("@rendermode", login);
    }

    [TestMethod]
    public void Every_page_declares_interactive_rendering()
    {
        // Without this the buttons render and do nothing: the .NET 10 blazor template serves pages
        // statically unless a component asks for interactivity.
        // Home is static too: it reads counts once and has nothing to click.
        foreach (var page in Pages().Where(f => f.Name != "Home.razor"))
            Assert.Contains("@rendermode InteractiveServer", page.Source, page.Name);
    }

    // ---- ScreenFields: the one part of this that can be compiled and run ----

    private static Assembly EmitFields()
    {
        var source = EmittedSource.Flatten(
            ModelStub,
            BlazorScreenTemplates.Fields("Shop").Replace("namespace Shop.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private const string ModelStub = """
public class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime OrderedOn { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedOn { get; set; }
}
""";

    private static object Invoke(Assembly assembly, string method, params object?[] arguments)
        => assembly.GetType("ScreenFields")!
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, arguments)!;

    private static string[] Names_(object columns)
        => [.. ((PropertyInfo[])columns).Select(p => p.Name)];

    [TestMethod]
    public void The_columns_put_the_id_first_and_leave_out_what_a_table_cannot_show()
    {
        var columns = Names_(Invoke(EmitFields(), "Columns", EmitFields().GetType("Order")));

        Assert.AreEqual("Id", columns[0]);
        CollectionAssert.Contains(columns, "Amount");
        CollectionAssert.Contains(columns, "OrderedOn");
    }

    /// <summary>Id is assigned by the store and shown by the list, so the editor never offers
    /// it.</summary>
    [TestMethod]
    public void The_editor_does_not_offer_the_id()
    {
        var assembly = EmitFields();

        CollectionAssert.DoesNotContain(
            Names_(Invoke(assembly, "Editable", assembly.GetType("Order"))), "Id");
    }

    /// <summary>
    /// The audit dates are not the date a user means.
    /// </summary>
    /// <remarks>
    /// AddAuditFields gives every model CreatedOn and ModifiedOn, so "the first DateTime" would
    /// filter every transaction screen on a field nobody chose and nobody sees.
    /// </remarks>
    [TestMethod]
    public void The_date_range_filters_on_the_models_own_date_not_the_audit_fields()
    {
        var assembly = EmitFields();

        Assert.AreEqual("OrderedOn", Invoke(assembly, "DateField", assembly.GetType("Order")));
    }

    /// <summary>Neither an Id nor a foreign key is money, however numeric it is.</summary>
    [TestMethod]
    public void The_totals_are_the_money_columns_only()
    {
        var assembly = EmitFields();

        CollectionAssert.AreEquivalent(
            new[] { "Amount" },
            Names_(Invoke(assembly, "MoneyColumns", assembly.GetType("Order"))));
    }

    [TestMethod]
    public void A_search_matches_across_every_string_column()
    {
        var assembly = EmitFields();
        var order = Order(assembly, reference: "Urgent");

        Assert.IsTrue((bool)Invoke(assembly, "Matches", order, "urge"));
        Assert.IsFalse((bool)Invoke(assembly, "Matches", order, "nothing"));

        // An empty search is not a filter.
        Assert.IsTrue((bool)Invoke(assembly, "Matches", order, string.Empty));
    }

    [TestMethod]
    public void The_range_excludes_what_falls_outside_it_and_keeps_a_record_with_no_date()
    {
        var assembly = EmitFields();
        var order = Order(assembly, orderedOn: new DateTime(2026, 6, 15));

        Assert.IsTrue((bool)Invoke(
            assembly, "InRange", order, "OrderedOn", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        Assert.IsFalse((bool)Invoke(
            assembly, "InRange", order, "OrderedOn", new DateTime(2026, 7, 1), null));

        // No date field named at all: a model with no date of its own is never filtered out.
        Assert.IsTrue((bool)Invoke(
            assembly, "InRange", order, string.Empty, new DateTime(2026, 7, 1), null));
    }

    [TestMethod]
    public void A_value_that_will_not_convert_is_reported_rather_than_swallowed()
    {
        var assembly = EmitFields();
        var order = Order(assembly);
        var amount = assembly.GetType("Order")!.GetProperty("Amount")!;

        Assert.IsNull(Invoke(assembly, "Assign", order, amount, "12.50"));
        Assert.AreEqual(12.50m, amount.GetValue(order));

        var complaint = (string?)assembly.GetType("ScreenFields")!
            .GetMethod("Assign")!.Invoke(null, [order, amount, "not a number"]);

        StringAssert.Contains(complaint!, "not a valid Amount");

        // And the old value survives the refusal.
        Assert.AreEqual(12.50m, amount.GetValue(order));
    }

    /// <summary>The copy is what makes Cancel mean something: nothing typed into the draft may
    /// reach the record behind it.</summary>
    [TestMethod]
    public void A_clone_is_independent_of_the_record_it_came_from()
    {
        var assembly = EmitFields();
        var orderType = assembly.GetType("Order")!;
        var original = Order(assembly, reference: "Original");

        var clone = assembly.GetType("ScreenFields")!
            .GetMethod("Clone")!.MakeGenericMethod(orderType).Invoke(null, [original])!;

        orderType.GetProperty("Reference")!.SetValue(clone, "Changed");

        Assert.AreEqual("Original", orderType.GetProperty("Reference")!.GetValue(original));
    }

    [TestMethod]
    public void A_date_is_shown_the_way_a_date_input_expects_it()
    {
        var assembly = EmitFields();

        Assert.AreEqual("2026-06-15", Invoke(assembly, "Display", new DateTime(2026, 6, 15)));
        Assert.AreEqual(string.Empty, Invoke(assembly, "Display", new object?[] { null }));
    }

    private static object Order(
        Assembly assembly, string reference = "R1", DateTime? orderedOn = null)
    {
        var type = assembly.GetType("Order")!;
        var order = Activator.CreateInstance(type)!;

        type.GetProperty("Reference")!.SetValue(order, reference);
        type.GetProperty("OrderedOn")!.SetValue(order, orderedOn ?? new DateTime(2026, 1, 1));

        return order;
    }
}
