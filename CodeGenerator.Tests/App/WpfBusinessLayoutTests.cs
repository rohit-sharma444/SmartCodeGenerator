using System.Xml.Linq;
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The business shell: one rail, one header, one content area, and no per-entity
/// XAML.</summary>
[TestClass]
public class WpfBusinessLayoutTests
{
    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            "Payroll", "Payroll", settings, ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    private static string Xaml(AppCustomization? customization = null)
        => new WpfBusinessLayout().ViewFiles(Context(customization))
            .Single(f => f.Name == "MainWindow.xaml").Source;

    /// <summary>
    /// The markup of one keyed <c>DataTemplate</c>, ending at its OWN closing tag, so a template's
    /// assertions cannot be satisfied by markup belonging to a different template.
    /// </summary>
    /// <remarks>
    /// Moving markup from one template to another leaves a whole-file <c>Contains</c> passing with
    /// the element in the wrong place, and an absence assertion passing vacuously — which is why
    /// every assertion about a template is scoped through here.
    /// <para>
    /// The end is COUNTED rather than cut at a marker, and both marker choices are why. Cutting at
    /// the first <c>&lt;/DataTemplate&gt;</c> fails closed but is wrong the moment a template grows
    /// a nested one. Cutting at what FOLLOWS the template — the selector — fails OPEN: adding a
    /// fifth template after this one silently widens the slice to swallow it, and every assertion
    /// scoped by it weakens with nothing going red to say so. Matching the tags gives the exact
    /// element under either change.
    /// </para>
    /// </remarks>
    private static string TemplateSlice(string key)
    {
        var xaml = Xaml();
        var from = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, from, $"no template keyed '{key}'");

        // The opening tag is already behind `from`, which points inside it.
        var depth = 1;
        var at = from;

        while (depth > 0)
        {
            var opened = xaml.IndexOf("<DataTemplate", at + 1, StringComparison.Ordinal);
            var closed = xaml.IndexOf("</DataTemplate>", at + 1, StringComparison.Ordinal);

            Assert.IsGreaterThan(-1, closed, $"'{key}' is never closed");

            if (opened >= 0 && opened < closed)
            {
                depth++;
                at = opened;

                continue;
            }

            depth--;
            at = closed;
        }

        return xaml.Substring(from, at - from);
    }

    /// <summary>The master screen's template.</summary>
    private static string MasterTemplate() => TemplateSlice("MasterTemplate");

    /// <summary>The editor's template, which holds nested DataTemplates of its own.</summary>
    private static string EditorTemplate() => TemplateSlice("EditorTemplate");

    /// <summary>The transaction screen's template.</summary>
    private static string TransactionTemplate() => TemplateSlice("TransactionTemplate");

    [TestMethod]
    public void The_registry_resolves_the_business_key()
    {
        Assert.AreEqual("business", WpfLayouts.Resolve(AppLayout.Business).Key);
        Assert.AreEqual("business", AppLayoutCatalog.KeyFor(AppLayout.Business));
    }

    /// <summary>Every enum value has an entry, or a build silently falls back to tabs.</summary>
    [TestMethod]
    public void Every_layout_value_has_a_catalogue_entry_and_a_wpf_layout()
    {
        foreach (AppLayout layout in Enum.GetValues<AppLayout>())
        {
            var key = AppLayoutCatalog.KeyFor(layout);

            Assert.AreEqual(key, WpfLayouts.Resolve(layout).Key, $"{layout} falls back");
        }
    }

    [TestMethod]
    public void The_shell_has_a_grouped_rail_a_header_and_one_content_area()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Groups}\"");
        StringAssert.Contains(xaml, "{Binding SignedInUser}");

        // A Click handler, not the command alone: the command asks before discarding an edit and
        // sets the flag, and something still has to CLOSE the window -- the sign-in loop in
        // App.xaml.cs only regains control when it does. Bound to the command alone, Sign out
        // looked dead in a running app.
        StringAssert.Contains(xaml, "Click=\"OnShellSignOut\"");
        StringAssert.Contains(xaml, "Content=\"{Binding Current}\"");
    }

    /// <summary>The file must not grow with the model. A per-entity name in here would mean a
    /// twenty-entity app had a twenty-times-longer window.</summary>
    [TestMethod]
    public void No_entity_name_appears_in_the_window()
    {
        var xaml = new WpfBusinessLayout().ViewFiles(Context()).Single(f => f.Name == "MainWindow.xaml").Source;

        Assert.IsFalse(xaml.Contains("Employee", StringComparison.Ordinal));
        Assert.IsFalse(xaml.Contains("Order", StringComparison.Ordinal));
    }

    [TestMethod]
    public void All_four_screens_have_a_template()
    {
        var xaml = Xaml();

        foreach (var key in new[]
                 { "DashboardTemplate", "MasterTemplate", "TransactionTemplate", "EditorTemplate" })
        {
            StringAssert.Contains(xaml, $"x:Key=\"{key}\"");
        }

        StringAssert.Contains(xaml, "ContentTemplateSelector=\"{StaticResource ScreenTemplate}\"");
    }

    /// <summary>
    /// The templates are selected, not resolved by DataType.
    /// </summary>
    /// <remarks>
    /// WPF resolves an implicit template by the value's EXACT type and cannot name an open
    /// generic, and every screen here is <c>MasterScreenViewModel&lt;T&gt;</c> or similar. A
    /// DataType-keyed template would silently match nothing and the content area would render the
    /// view model's type name.
    /// </remarks>
    [TestMethod]
    public void No_template_is_keyed_by_an_open_generic_type()
    {
        var xaml = Xaml();

        Assert.IsFalse(
            xaml.Contains("DataType=", StringComparison.Ordinal),
            "DataType cannot name an open generic; these screens are all generic");
    }

    [TestMethod]
    public void The_transaction_template_has_the_range_and_the_totals()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "SelectedDate=\"{Binding From}\"");
        StringAssert.Contains(xaml, "SelectedDate=\"{Binding To}\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Totals}\"");
    }

    /// <summary>The master and editor templates have always bound it. A refusal written into
    /// Status by the delete handler is invisible on this screen until it does too.</summary>
    [TestMethod]
    public void The_transaction_template_shows_the_status_line()
        => StringAssert.Contains(TransactionTemplate(), "{Binding Status}");

    /// <summary>The master mockup's shape: one screen holding the form and the list.</summary>
    [TestMethod]
    public void The_master_template_holds_a_header_a_form_and_a_list()
    {
        var master = MasterTemplate();

        StringAssert.Contains(master, "<views:EntryHeader");
        StringAssert.Contains(master, "<views:EntryForm");
        StringAssert.Contains(master, "<TabControl");
        StringAssert.Contains(master, "<DataGrid");
    }

    [TestMethod]
    public void The_master_template_binds_the_inline_editor()
        => StringAssert.Contains(MasterTemplate(), "Record=\"{Binding Editor.Draft}\"");

    [TestMethod]
    public void The_action_bar_offers_only_commands_that_exist()
    {
        var master = MasterTemplate();

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(master, command);
        }
    }

    /// <summary>Spec departure 1: a button that does nothing is output that misinforms.</summary>
    [TestMethod]
    public void No_button_without_behaviour_is_emitted()
    {
        var xaml = Xaml();

        foreach (var absent in new[] { "Print", "Export", "Save &amp; Print", "Import Items" })
            Assert.DoesNotContain($"Content=\"{absent}\"", xaml);
    }

    /// <summary>
    /// The list finds; the entry form changes. A typeable list would put the application back to
    /// editing two different ways.
    /// </summary>
    /// <remarks>
    /// Scoped to each template rather than counted across the file: the count assertion this
    /// replaces stayed green when a grid moved from one template to another, and said nothing
    /// about WHICH grid was read-only. The lines grid IS the editor for a line, which has no
    /// screen of its own, so it is the one grid that must NOT be read-only.
    /// </remarks>
    [TestMethod]
    public void The_master_list_is_read_only_and_the_lines_grid_is_not()
    {
        StringAssert.Contains(MasterTemplate(), "IsReadOnly=\"True\"");
        Assert.DoesNotContain("IsReadOnly", EditorTemplate());
    }

    [TestMethod]
    public void The_editor_template_holds_the_lines_in_tabs_with_their_totals()
    {
        var editor = EditorTemplate();

        StringAssert.Contains(editor, "<views:EntryHeader");
        StringAssert.Contains(editor, "<views:EntryForm");
        StringAssert.Contains(editor, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(editor, "ItemsSource=\"{Binding Totals}\"");
        StringAssert.Contains(editor, "OnClearLines");
        StringAssert.Contains(editor, "<views:EntryNotes");

        // Save and Cancel, asserted inside the slice: the master's action bar carries both names
        // too, so a whole-file assertion passed with the editor's bar deleted outright.
        StringAssert.Contains(editor, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(editor, "Command=\"{Binding CancelCommand}\"");
        StringAssert.Contains(editor, "Click=\"OnAddLine\"");
        StringAssert.Contains(editor, "Click=\"OnRemoveLine\"");
    }

    /// <summary>
    /// The lines grid reports a committed cell edit, or the totals beside it go stale.
    /// </summary>
    /// <remarks>
    /// The one line-change a set cannot see for itself: the grid writes the typed value straight
    /// into the line, which is a plain generated model. Without this attribute a purchase order
    /// shows the OLD total after a quantity is typed — the most visible number on the screen,
    /// silently wrong — and nothing else in this suite moves.
    /// </remarks>
    [TestMethod]
    public void The_lines_grid_reports_a_committed_cell_edit()
        => StringAssert.Contains(EditorTemplate(), "CellEditEnding=\"OnLineCellEdited\"");

    /// <summary>
    /// The notes section on the editor is capped, and scrolls inside its cap.
    /// </summary>
    /// <remarks>
    /// It is docked to the bottom of a DockPanel, so it takes its whole desired height before the
    /// lines grid — the fill child — gets any, and EntryNotes asks for 90px per long-text field
    /// with no ceiling. Uncapped, a document with three of them arranges the lines at zero height
    /// on this window's default 640, and a DockPanel clips rather than scrolls: the lines simply
    /// are not there. Nothing else in this suite can see that, which is why the cap is pinned here.
    /// </remarks>
    [TestMethod]
    public void The_editors_notes_section_is_capped_and_scrolls()
    {
        var editor = EditorTemplate();

        StringAssert.Contains(editor, "MaxHeight=\"180\"");
        StringAssert.Contains(editor, "<ScrollViewer VerticalScrollBarVisibility=\"Auto\">");
    }

    /// <summary>The long-text fields are reachable on both screens, and on neither of them are
    /// they also in a panel — FieldGroups.For excludes them, and this is the view half of that.</summary>
    [TestMethod]
    public void Both_screens_show_the_additional_fields_behind_HasAdditional()
    {
        StringAssert.Contains(MasterTemplate(), "Header=\"Additional Information\"");
        StringAssert.Contains(EditorTemplate(), "Text=\"Additional Information\"");
        StringAssert.Contains(Xaml(), "<BooleanToVisibilityConverter x:Key=\"BoolToVisibility\" />");
    }

    /// <summary>
    /// The three areas that exist only for some models are BOUND to the property that says so.
    /// </summary>
    /// <remarks>
    /// This layout emits no per-entity XAML, so an Additional Information tab, its editor-side
    /// section and the lines area cannot be emitted only for the models that have them: each is
    /// drawn always and hidden by a binding. Delete the three attributes and a model with no
    /// long-text fields grows an empty tab and an empty titled section, and a master's editor grows
    /// an empty lines area — with every other test in this file green, and the reflection test
    /// unmoved (33 paths become 30, still above its floor).
    /// <para>
    /// The needle is the whole attribute, not the property name: the editor's own comment says "which
    /// is what HasLines says", so a bare <c>HasLines</c> is satisfied by prose after the binding is
    /// gone. Scoped per template as well, because all three would otherwise be interchangeable.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Every_optional_area_is_hidden_by_the_property_that_says_it_is_empty()
    {
        const string additional =
            "Visibility=\"{Binding HasAdditional, Converter={StaticResource BoolToVisibility}}\"";

        StringAssert.Contains(MasterTemplate(), additional);
        StringAssert.Contains(EditorTemplate(), additional);
        StringAssert.Contains(
            EditorTemplate(),
            "Visibility=\"{Binding HasLines, Converter={StaticResource BoolToVisibility}}\"");
    }

    /// <summary>
    /// The heading on both bands is the one string the generated app already holds for that tab.
    /// </summary>
    /// <remarks>
    /// Two constants for one string is how this branch's other headings drifted: the layout must
    /// read <c>FieldGroupsTemplate.AdditionalTitle</c>, which is also what the emitted
    /// <c>FieldGroups.AdditionalTitle</c> the WinForms layout reads is built from, rather than
    /// spelling it a third time here.
    /// </remarks>
    [TestMethod]
    public void The_additional_heading_comes_from_the_one_constant_that_names_it()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, $"Header=\"{FieldGroupsTemplate.AdditionalTitle}\"");
        StringAssert.Contains(xaml, $"Text=\"{FieldGroupsTemplate.AdditionalTitle}\"");
        StringAssert.Contains(
            FieldGroupsTemplate.Source("Payroll"),
            $"public const string AdditionalTitle = \"{FieldGroupsTemplate.AdditionalTitle}\";");
    }

    // ---- what the layout registers (Task 9, R6) ----

    /// <summary>
    /// All THREE entry controls are registered, because both rewritten templates name all three.
    /// </summary>
    /// <remarks>
    /// A layout owns the files its views name. Registering two of the three emits an application
    /// whose MainWindow.xaml references a <c>views:EntryNotes</c> that no file declares, which is
    /// a compile error in the generated app rather than a failure here — the exact way a green
    /// test suite has shipped a broken app on this branch before.
    /// </remarks>
    [TestMethod]
    public void The_layout_emits_the_three_entry_controls()
    {
        var names = new WpfBusinessLayout().ViewFiles(Context()).Select(f => f.Name).ToList();
        var master = MasterTemplate();
        var editor = EditorTemplate();

        foreach (var file in new[]
                 {
                     WpfEntryControls.HeaderFileName, WpfEntryControls.FormFileName,
                     WpfEntryControls.NotesFileName,
                 })
        {
            var element = $"<views:{file.Replace(".cs", string.Empty)}";

            Assert.Contains(file, names);

            // Scoped to the two templates that name them, not to the file: a whole-file assertion
            // is satisfied by a control that appears only in TransactionTemplate, which would leave
            // an entry screen missing the control while the registration test stayed green.
            StringAssert.Contains(master, element);
            StringAssert.Contains(editor, element);
        }
    }

    /// <summary>
    /// The flat property editor is gone from THIS layout.
    /// </summary>
    /// <remarks>
    /// Not deleted as a file: the four pre-business layouts still emit it, and their neutrality
    /// tests pin it byte for byte. Emitting it here as well would ship a second editor for the
    /// same record that nothing draws.
    /// </remarks>
    [TestMethod]
    public void The_business_layout_no_longer_emits_the_flat_property_editor()
    {
        var names = new WpfBusinessLayout().ViewFiles(Context()).Select(f => f.Name).ToList();

        Assert.DoesNotContain(PropertyEditorTemplate.FileName, names);
        Assert.DoesNotContain("<views:PropertyEditor", Xaml());
    }

    // ---- the bindings resolve against the real view models (Task 9, R36) ----

    /// <summary>
    /// The window must be well-formed XML, or the generated app does not build at all.
    /// </summary>
    /// <remarks>
    /// XAML is read by an XML reader, and XML forbids the string <c>--</c> inside a comment. Two
    /// hyphens in an explanatory comment — the natural way this repository writes an aside — is
    /// therefore a build error in every generated app, which no substring assertion over the
    /// emitted text can see.
    /// </remarks>
    [TestMethod]
    public void The_window_is_well_formed_xml()
    {
        foreach (var xaml in new[]
                 {
                     Xaml(), Xaml(AppCustomization.Default with { Theme = AppTheme.Dark }),
                     Xaml(AppCustomization.Default with { EntryTemplate = AppScaffolding.Screens.EntryTemplate.Ribbon }),
                 })
        {
            Assert.IsNotNull(XDocument.Parse(xaml).Root);
        }
    }

    /// <summary>
    /// Every binding path the two entry templates declare, resolved against the view models the
    /// application really emits.
    /// </summary>
    /// <remarks>
    /// The substring assertions above prove a string is in the file and nothing more. A WPF
    /// binding to a property that does not exist throws nothing, logs nothing and renders blank,
    /// so <c>Record="{Binding Editor.Draft}"</c> is satisfied whether or not anything answers it —
    /// and Task 6 reshaped exactly this surface.
    /// <para>
    /// The paths are EXTRACTED from the markup rather than listed here, so a binding added later
    /// is covered without this test being touched, and each is walked segment by segment against
    /// the compiled <c>MasterScreenViewModel&lt;Book&gt;</c> / <c>RecordEditorViewModel&lt;Book&gt;</c>
    /// — not against this fixture's own stubs, which would prove only that the view agrees with
    /// the stub.
    /// </para>
    /// <para>
    /// A nested DataTemplate is resolved against the ITEM type of the nearest ItemsSource above it,
    /// which is how the lines tab strip's <c>Title</c> is known to be ILineSet's rather than the
    /// editor's.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Every_binding_in_the_two_entry_templates_resolves_on_the_emitted_view_models()
    {
        var assembly = MasterScreenTests.Emit();
        var book = assembly.GetType("Book")!;

        var screen = assembly.GetType("MasterScreenViewModel`1")!.MakeGenericType(book);
        var editor = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(book);

        var window = XDocument.Parse(Xaml()).Root!;
        var unresolved = new List<string>();

        var checked_ = CheckBindings(TemplateKeyed(window, "MasterTemplate"), screen, unresolved)
                       + CheckBindings(TemplateKeyed(window, "EditorTemplate"), editor, unresolved);

        Assert.IsEmpty(unresolved, $"bindings nothing answers: {string.Join("; ", unresolved)}");

        // Or an extraction that quietly found nothing would report every binding as correct.
        Assert.IsGreaterThan(24, checked_, "far fewer bindings than these two templates declare");
    }

    /// <summary>The keyed template of one screen, as XML.</summary>
    private static XElement TemplateKeyed(XElement window, string key)
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        return window.Descendants()
            .Single(e => e.Name.LocalName == "DataTemplate" && (string?)e.Attribute(x + "Key") == key);
    }

    /// <summary>Resolves every binding in one DataContext scope, and returns how many it checked.</summary>
    private static int CheckBindings(XElement scope, Type context, ICollection<string> unresolved)
    {
        var count = 0;

        foreach (var attribute in scope.Attributes())
        {
            if (BindingPathOf(attribute.Value) is not { } path)
                continue;

            count++;

            if (Resolve(context, path) is null)
                unresolved.Add($"{context.Name}.{path} ({scope.Name.LocalName}.{attribute.Name.LocalName})");
        }

        foreach (var child in scope.Elements())
        {
            count += child.Name.LocalName == "DataTemplate"
                ? CheckBindings(child, ItemTypeOf(child, context), unresolved)
                : CheckBindings(child, context, unresolved);
        }

        return count;
    }

    /// <summary>The path a binding declares, or null for markup that is not a binding.</summary>
    private static string? BindingPathOf(string value)
    {
        const string opener = "{Binding ";

        if (!value.StartsWith(opener, StringComparison.Ordinal))
            return null;

        var rest = value[opener.Length..];
        var end = rest.IndexOfAny([',', '}']);
        var path = (end < 0 ? rest : rest[..end]).Trim();

        // A form this extractor does not understand must fail rather than be skipped, or the
        // binding it hides is silently reported as correct.
        Assert.IsGreaterThan(0, path.Length, $"a binding with no path: {value}");
        Assert.IsLessThan(0, path.IndexOf('='), $"an unhandled binding form: {value}");

        return path;
    }

    /// <summary>The item type a nested DataTemplate is applied to: what the nearest ItemsSource
    /// above it holds.</summary>
    private static Type ItemTypeOf(XElement template, Type context)
    {
        var source = template.Ancestors()
            .Select(a => a.Attribute("ItemsSource")?.Value)
            .FirstOrDefault(v => v is not null);

        Assert.IsNotNull(source, "a nested DataTemplate with no ItemsSource above it");

        var path = BindingPathOf(source!);

        Assert.IsNotNull(path, $"an ItemsSource that is not a binding: {source}");

        var collection = Resolve(context, path!);

        Assert.IsNotNull(collection, $"ItemsSource '{path}' does not resolve on {context.Name}");
        Assert.IsTrue(
            collection!.IsGenericType,
            $"{collection.Name} does not say what it holds, so '{path}' cannot be checked");

        return collection.GetGenericArguments()[0];
    }

    /// <summary>The type one binding path ends at, walked segment by segment, or null where a
    /// segment names nothing.</summary>
    private static Type? Resolve(Type context, string path)
    {
        var current = context;

        foreach (var segment in path.Split('.'))
        {
            var property = current.GetProperty(segment);

            if (property is null)
                return null;

            current = property.PropertyType;
        }

        return current;
    }

    [TestMethod]
    public void The_line_buttons_act_on_the_set_they_belong_to()
    {
        var code = new WpfBusinessLayout().ViewFiles(Context())
            .Single(f => f.Name == "MainWindow.xaml.cs").Source;

        StringAssert.Contains(code, "private void OnAddLine(");
        StringAssert.Contains(code, "private void OnRemoveLine(");
        StringAssert.Contains(code, "private void OnClearLines(");
        StringAssert.Contains(code, "lines.Clear()");
        StringAssert.Contains(code, "private void OnLineCellEdited(");

        // The whole call, not the name: the dispatcher hop is the load-bearing half. CellEditEnding
        // runs BEFORE the cell's binding writes the typed value, so a total recomputed inside the
        // handler sums the values the grid is about to replace -- a wrong number rather than a
        // stale one, which is worse.
        StringAssert.Contains(code, "Dispatcher.BeginInvoke(new Action(lines.LineEdited))");

        // The DataContext of the button, the same way the rail's buttons carry their screen.
        StringAssert.Contains(code, "DataContext: ILineSet lines");
        StringAssert.Contains(code, "lines.Remove(lines.Selected)");
    }

    /// <summary>
    /// The line handlers call only members the emitted <c>ILineSet</c> really declares.
    /// </summary>
    /// <remarks>
    /// <see cref="RoleStub"/> is this fixture's own hand-written copy of that interface, so
    /// <see cref="The_selector_and_the_code_behind_compile"/> would happily compile a handler
    /// against a member only the stub has — the "a stub thinner (or fatter) than the type it
    /// stands in for" failure this file already records once. The real declaration is asserted
    /// here instead.
    /// </remarks>
    [TestMethod]
    public void The_line_handlers_call_only_members_the_emitted_line_set_declares()
    {
        var lineSet = LineSetTemplate.Source("Payroll");

        foreach (var member in new[] { "void Add();", "void Remove(object? line);", "void Clear();", "void LineEdited();" })
            StringAssert.Contains(lineSet, member);
    }

    /// <summary>Theming stays orthogonal, as it is for every other layout.</summary>
    [TestMethod]
    public void A_themed_shell_binds_the_theme_brushes()
    {
        var themed = Xaml(AppCustomization.Default with { Theme = AppTheme.Dark });

        StringAssert.Contains(themed, "{DynamicResource AppSurfaceBrush}");
        StringAssert.Contains(themed, "{DynamicResource AppBackgroundBrush}");
    }

    [TestMethod]
    public void An_unthemed_shell_uses_the_same_neutrals_the_sidebar_uses()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "#F9FAFB");
        StringAssert.Contains(xaml, "#E5E7EB");
    }

    [TestMethod]
    public void The_code_behind_wires_navigation_and_the_discard_prompt()
    {
        var code = new WpfBusinessLayout().ViewFiles(Context())
            .Single(f => f.Name == "MainWindow.xaml.cs").Source;

        StringAssert.Contains(code, "private void OnNavigate(");
        StringAssert.Contains(code, "shell.TryShow(screen)");
        StringAssert.Contains(code, "OnAutoGeneratingColumn");

        // The window binds the SHELL. Without this it bound the pane list every other layout
        // binds, so the rail and the content area bound to properties MainViewModel does not have
        // and the window opened showing a header and nothing else. SignedInUser resolved anyway,
        // because MainViewModel happens to have one too, which is what made it look plausible.
        StringAssert.Contains(code, "DataContext = shell;");
        StringAssert.Contains(code, "new ShellViewModel(");

        // And the prompt this shell exists to be able to ask.
        StringAssert.Contains(code, "shell.ConfirmDiscard = () => MessageBox.Show(");
        StringAssert.Contains(code, "\"Discard your changes?\"");

        // Sign out runs the command and then closes, or the flag is set and nothing acts on it.
        StringAssert.Contains(code, "private void OnShellSignOut(");
        StringAssert.Contains(code, "if (Session.SignOutRequested)");
        StringAssert.Contains(code, "Close();");
    }

    /// <summary>
    /// Every C# file this layout emits, compiled as one assembly.
    /// </summary>
    /// <remarks>
    /// The entry controls are in that set now, so FieldGroups and RequiredFields travel with it:
    /// they are what those controls reflect over, and a generated app really does have both.
    /// </remarks>
    [TestMethod]
    public void The_selector_and_the_code_behind_compile()
    {
        var files = new WpfBusinessLayout().ViewFiles(Context());

        EmittedDesktopSource.AssertCompilesTogether(
            [
                .. files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                        .Select(f => (f.Name, f.Source)),
                ("Views/MainWindow.g.cs", XamlPartialStub),
                ("Screens/EntityRole.cs", RoleStub),
                ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
                ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
                ("ViewModels/Shell.cs", ShellStub),
                ("Auth/Session.cs", SessionStub),
            ]);
    }

    /// <summary>What the XAML compiler generates, which Roslyn alone cannot.</summary>
    private const string XamlPartialStub = """
        namespace Payroll.Views;

        public partial class MainWindow
        {
            private void InitializeComponent() { }
        }
        """;

    /// <summary>Just enough of the view models for the code-behind and the selector to bind.
    /// Separate files, because a compilation unit may not mix file-scoped and block
    /// namespaces.</summary>
    private const string RoleStub = """
        namespace Payroll.Screens;

        public enum EntityRole { Master, Transaction, DocumentLine }

        /// <summary>The lines of a transaction, as much of it as a view touches.</summary>
        public interface ILineSet
        {
            string Title { get; }
            string ForeignKey { get; }
            System.Type LineType { get; }
            System.Collections.IEnumerable Items { get; }
            object? Selected { get; set; }
            bool IsDirty { get; }
            void Load(int parentId);
            void Add();
            void Remove(object? line);
            void Clear();
            void LineEdited();
            bool Save(int parentId);
        }
        """;

    private const string ShellStub = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Windows.Input;
        using Payroll.Screens;

        namespace Payroll.ViewModels;

        public interface IEntityViewModel
        {
            string Title { get; }
            IEnumerable Items { get; }
            object? Selected { get; set; }
            ICommand AddCommand { get; }
            ICommand DeleteCommand { get; }
            ICommand SaveCommand { get; }
            string Status { get; }
            EntityRole Role { get; }
            int Count { get; }
        }

        public sealed record LineTotal(string Column, decimal Value, bool IsGrand);

        public interface IDirtyScreen
        {
            bool IsDirty { get; }
        }

        public interface IRecordEditor : IDirtyScreen
        {
            string Title { get; }
            IReadOnlyList<ILineSet> Lines { get; }
            IReadOnlyList<LineTotal> Totals { get; }
        }

        public sealed class DashboardViewModel
        {
        }

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();
            public string SignedInUser => string.Empty;
        }

        public sealed class NavGroup
        {
            public string Title => string.Empty;
            public IReadOnlyList<IEntityViewModel> Entities { get; } = new List<IEntityViewModel>();
        }

        /// <summary>
        /// The shape the real ShellViewModel has, not the one line the code-behind used to touch.
        /// </summary>
        /// <remarks>
        /// A stub thinner than the type it stands in for is how a window that never built a shell
        /// at all compiled here for a week.
        /// </remarks>
        public sealed class ShellViewModel
        {
            public ShellViewModel(
                IReadOnlyList<IEntityViewModel> entities, IReadOnlyList<IEntityViewModel> adminEntities)
            {
            }

            public Func<bool> ConfirmDiscard { get; set; } = () => true;
            public DashboardViewModel Dashboard { get; } = new();
            public IReadOnlyList<NavGroup> Groups { get; } = new List<NavGroup>();
            public string SignedInUser => string.Empty;
            public object? Current => Dashboard;
            public bool CanGoBack => false;
            public ICommand SignOutCommand { get; } = null!;
            public ICommand BackCommand { get; } = null!;
            public ICommand HomeCommand { get; } = null!;
            public bool TryShow(object screen) => true;
            public void Back() { }
        }
        """;

    /// <summary>The session the emitted sign-out handler calls.</summary>
    private const string SessionStub = """
        namespace Payroll.Auth;

        public static class Session
        {
            public static bool SignOutRequested { get; private set; }
            public static void RequestSignOut() => SignOutRequested = true;
        }
        """;

    /// <summary>
    /// The lines grid does not offer the foreign key that ties a line to the document it is
    /// being edited inside.
    /// </summary>
    /// <remarks>
    /// The shared column rules turn a &lt;Nav&gt;Id into a combo box over that entity's rows, which
    /// is right on a list — a user picks a Customer rather than typing its id — and wrong here:
    /// the parent is the document on screen, LineSet stamps it on save, and a picker over every
    /// other document invites reassigning a line to one of them. WinForms hides the column
    /// (`column.Visible = false`) and the web filters the field out; WPF showed it, which is how
    /// this was found — by opening a generated app and reading the grid.
    /// </remarks>
    [TestMethod]
    public void The_lines_grid_hides_the_foreign_key()
    {
        var code = new WpfBusinessLayout().ViewFiles(Context())
            .Single(f => f.Name == "MainWindow.xaml.cs").Source;

        // The set on the row knows its own key name, so the rule needs no entity name: whatever
        // was auto-generated is compared against the ILineSet the grid is bound to.
        StringAssert.Contains(code, "DataContext: ILineSet");
        StringAssert.Contains(code, "lines.ForeignKey");
        StringAssert.Contains(code, "e.Cancel = true");
    }

    /// <summary>
    /// The guard runs before the relationship column is built, or the combo box is created and
    /// then thrown away — and a cancelled column that was already replaced does not cancel.
    /// </summary>
    [TestMethod]
    public void The_foreign_key_guard_precedes_the_relationship_column()
    {
        var code = new WpfBusinessLayout().ViewFiles(Context())
            .Single(f => f.Name == "MainWindow.xaml.cs").Source;

        var guard = code.IndexOf("lines.ForeignKey", StringComparison.Ordinal);
        var relationship = code.IndexOf("TryBuildRelationshipColumn(sender, e)", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, guard);
        Assert.IsGreaterThan(-1, relationship);
        Assert.IsLessThan(relationship, guard, "the combo box is built before the key is dropped");
    }

    /// <summary>
    /// Only the business shell can mention ILineSet: the four older layouts share this
    /// code-behind and never emit Screens/LineSet.cs, so naming the type there is a compile
    /// error in the generated app rather than a test failure here.
    /// </summary>
    [TestMethod]
    public void No_older_layout_mentions_the_line_set()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var code = WpfLayouts.Resolve(layout).ViewFiles(Context())
                .SingleOrDefault(f => f.Name == "MainWindow.xaml.cs")?.Source;

            if (code is null)
                continue;

            Assert.DoesNotContain("ILineSet", code, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void The_entry_controls_compile()
    {
        var context = Context();

        EmittedDesktopSource.AssertCompilesTogether(
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
            ($"Views/{WpfEntryControls.HeaderFileName}", WpfEntryControls.Header(context)),
            ($"Views/{WpfEntryControls.FormFileName}", WpfEntryControls.Form(context)),
            ($"Views/{WpfEntryControls.NotesFileName}", WpfEntryControls.Notes(context)));
    }

    /// <summary>
    /// A foreign key is a lookup, not a text box -- dropping it would leave no way to set a
    /// document's supplier at all.
    /// </summary>
    /// <remarks>
    /// Needles unique to the foreign-key branch: <c>new ComboBox</c> alone also matches the enum
    /// branch, so replacing this branch's body with <c>return new TextBox();</c> (keeping the
    /// <c>if</c>) passed a version of this test that asserted only that pair. "FieldGroups.NavigationFor("
    /// without the parameter name survives a parameter rename. The bare word
    /// <c>SelectedValuePath</c> appears in that branch's own explanatory comment as well, so the
    /// needle is the ASSIGNMENT: swapping the ComboBox for a TextBox and leaving the comment
    /// behind passed the version of this test that asserted only the word.
    /// </remarks>
    [TestMethod]
    public void The_form_renders_a_foreign_key_as_a_lookup()
    {
        var source = WpfEntryControls.Form(Context());

        StringAssert.Contains(source, "FieldGroups.NavigationFor(");
        StringAssert.Contains(source, "SelectedValuePath = \"Id\"");
    }

    /// <summary>
    /// The required marker sits in a POSITIVE guard, so the guard cannot be inverted (asterisking
    /// every optional field) or short-circuited to a discard (<c>_ = RequiredFields.IsRequired(...)</c>,
    /// with the asterisk never added) without failing.
    /// </summary>
    [TestMethod]
    public void The_form_marks_required_fields()
    {
        var source = WpfEntryControls.Form(Context());

        StringAssert.Contains(source, "if (RequiredFields.IsRequired(");
        StringAssert.Contains(source, "\" *\"");
    }

    /// <summary>
    /// The forbidden set is widened past the two entity names the brief names ("Customer" and
    /// "Order"): <see cref="Context"/> carries no entity list at all, so those two exact words could
    /// never appear under any implementation except a literal hardcode of them. "Book" is the name
    /// nearly every other fixture in this repository uses, and would sail straight through a check
    /// that stopped at "Customer" and "Order".
    /// </summary>
    [TestMethod]
    public void No_entity_name_appears_in_any_control()
    {
        var forbidden = new[]
        {
            "Customer", "Order", "Book", "Supplier", "PurchaseOrder", "Ticket", "Employee",
            "ShipToLocation",
        };

        foreach (var source in new[]
                 {
                     WpfEntryControls.Header(Context()), WpfEntryControls.Form(Context()),
                     WpfEntryControls.Notes(Context()),
                 })
        {
            foreach (var name in forbidden)
                Assert.DoesNotContain(name, source);
        }
    }

    // ---- each control genuinely emits content (fix round 1, Important 4) ----

    /// <summary>Catches the three-column loop being replaced by a single column that stacks every
    /// group without ever asking FieldGroups how to pack them.</summary>
    [TestMethod]
    public void The_form_lays_out_the_columns_FieldGroups_packs()
        => StringAssert.Contains(WpfEntryControls.Form(Context()), "FieldGroups.Columns(");

    /// <summary>Catches EntryHeader being reduced to an empty Grid: it compiles, and the
    /// no-entity-name test passes trivially, but the header would show no key fields at all.</summary>
    [TestMethod]
    public void The_header_reads_the_records_key_fields()
        => StringAssert.Contains(WpfEntryControls.Header(Context()), "FieldGroups.Key(");

    /// <summary>Catches EntryNotes.Build() being emptied out: it compiles, and a record with
    /// long-text fields would show no Additional Information content at all.</summary>
    [TestMethod]
    public void The_notes_tab_reads_the_long_text_fields_FieldGroups_finds()
        => StringAssert.Contains(WpfEntryControls.Notes(Context()), "FieldGroups.Additional(");

    /// <summary>
    /// Catches every <c>SetBinding</c> call being deleted from EntryForm: every editor branch
    /// (text, check, date, enum, and the foreign-key lookup) attaches its control through one, and
    /// deleting them all would leave every field on the form permanently blank no matter what the
    /// record held.
    /// </summary>
    /// <remarks>
    /// The needle is <c>.SetBinding(</c> with the leading dot, not bare <c>SetBinding</c>: this
    /// file's own doc comments mention the word "SetBinding" in prose (see the remarks on
    /// <c>Editor</c>'s foreign-key branch), so the bare word matches even after every real call is
    /// deleted -- caught only by actually running the mutation, which is why this needle is dotted.
    /// </remarks>
    [TestMethod]
    public void The_form_binds_every_field_it_draws()
        => StringAssert.Contains(WpfEntryControls.Form(Context()), ".SetBinding(");

    /// <summary>The same guard as <see cref="The_form_binds_every_field_it_draws"/>, for the one
    /// binding EntryNotes builds directly rather than through EntryForm's shared editor.</summary>
    [TestMethod]
    public void The_notes_tab_binds_the_fields_it_draws()
        => StringAssert.Contains(WpfEntryControls.Notes(Context()), ".SetBinding(");

    /// <summary>
    /// EntryHeader has no <c>SetBinding</c> call of its own after fix round 1 (Important 9): its
    /// key strip is built by <see cref="EntryForm.Editor"/>, so the equivalent "this control still
    /// emits real content" guard is that the delegation itself is present -- deleting it (leaving
    /// the key strip drawing labels with no editors under them) is what this catches. Deleting a
    /// <c>SetBinding</c> call INSIDE EntryForm.Editor is caught by
    /// <see cref="The_form_binds_every_field_it_draws"/> instead, since that is where the call
    /// actually lives now.
    /// </summary>
    [TestMethod]
    public void The_header_wires_its_key_fields_through_the_shared_editor()
        => StringAssert.Contains(WpfEntryControls.Header(Context()), "EntryForm.Editor(");

    // ---- fix round 1: behaviour, covered ----

    /// <summary>
    /// Critical 1. The themed branch used to hand the literal string
    /// <c>"{DynamicResource AppAccentBrush}"</c> to <c>BrushConverter.ConvertFromString</c>, which
    /// parses colour strings only and threw FormatException the first time a record reached a
    /// themed app's entry screen. It must now hand the resource KEY to SetResourceReference
    /// instead, in both controls that carry a themed brush.
    /// </summary>
    [TestMethod]
    public void A_themed_context_sets_the_accent_by_resource_reference_not_a_converter()
    {
        var themed = Context(AppCustomization.Default with { Theme = AppTheme.Dark });

        foreach (var source in new[] { WpfEntryControls.Header(themed), WpfEntryControls.Form(themed) })
        {
            Assert.DoesNotContain("ConvertFromString(\"{DynamicResource", source);
            StringAssert.Contains(source, "SetResourceReference(property, \"AppAccentBrush\")");
        }
    }

    /// <summary>The unthemed branch is the one every other test in this file exercises through
    /// <see cref="Context"/>; asserted here too so both halves of Themed are covered by name in one
    /// place next to the themed assertion above.</summary>
    [TestMethod]
    public void An_unthemed_context_still_uses_the_plain_hex_accent()
        => StringAssert.Contains(WpfEntryControls.Header(Context()), "#2563EB");

    /// <summary>
    /// Important 5. DisplayMemberPath used to be hardcoded to "Id", which resolves -- there is no
    /// binding error -- and renders a lookup of bare integers instead of the name a person
    /// recognises.
    /// </summary>
    /// <remarks>
    /// Reading the code field was only half a fix, and running a generated app is what showed it:
    /// a Supplier whose only label is Name has no code field, the "?? Id" fell through, and the
    /// picker listed 1, 2, 3 on the very screen the mockup chooses a supplier on. The choice now
    /// belongs to FieldGroups.LookupLabel, which both desktop targets call -- so this asserts the
    /// CALL, and FieldGroupsTests compiles and runs the rule behind it.
    /// </remarks>
    [TestMethod]
    public void The_lookup_displays_what_a_person_calls_the_record_not_its_id()
        => StringAssert.Contains(
            WpfEntryControls.Form(Context()),
            "DisplayMemberPath = FieldGroups.LookupLabel(nav.PropertyType)");

    /// <summary>
    /// Important 6. ItemsSource, DisplayMemberPath and SelectedValuePath must all be set before the
    /// SelectedValue binding is attached -- a Selector cannot resolve a bound value against a path
    /// or a list that do not exist yet, and attaching the binding first left an existing record's
    /// picker blank.
    /// </summary>
    [TestMethod]
    public void The_lookups_items_and_paths_are_set_before_its_binding_is_attached()
    {
        var source = WpfEntryControls.Form(Context());

        var itemsSource = source.IndexOf("ItemsSource = Rows(", StringComparison.Ordinal);
        var setBinding = source.IndexOf("lookup.SetBinding(", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, itemsSource);
        Assert.IsGreaterThan(-1, setBinding);
        Assert.IsLessThan(
            setBinding, itemsSource,
            "the binding was attached before the lookup's items source existed");
    }

    /// <summary>
    /// Important 7. The lookup must read AllRecords, not Items: Items is the search-narrowed list a
    /// screen shows, so a picker built on it would silently drop every row that does not match text
    /// left in that OTHER screen's own search box.
    /// </summary>
    [TestMethod]
    public void The_lookup_reads_the_screens_full_list_not_its_search_filtered_one()
    {
        var source = WpfEntryControls.Form(Context());

        StringAssert.Contains(source, "GetProperty(\"AllRecords\")");
        Assert.DoesNotContain("GetProperty(\"Items\")", source);
    }

    /// <summary>
    /// Important 9, the other half: the key strip fields must go through the same type-aware editor
    /// as an ordinary panel field, so a DateTime key gets a DatePicker and an enum key gets a
    /// ComboBox rather than a free-text box an unparseable value silently fails to save.
    /// </summary>
    [TestMethod]
    public void The_header_key_strip_gets_type_aware_editors()
        => StringAssert.Contains(WpfEntryControls.Header(Context()), "EntryForm.Editor(property, record)");

    /// <summary>
    /// Minor 11. An EntryHeader whose Record is never set must still draw its band: the invariant
    /// chrome (height, background, columns) belongs in the constructor, not only inside the change
    /// callback.
    /// </summary>
    /// <remarks>
    /// The ACCENT is pinned as well as the height, because the accent is the symptom that rule
    /// exists to prevent: a band drawn with no background at all. Moving the height alone back
    /// into <c>Build</c> is the harmless half of the mutation, and pinning only the height left
    /// the harmful half green.
    /// </remarks>
    [TestMethod]
    public void The_headers_invariant_chrome_is_set_in_the_constructor()
    {
        var source = WpfEntryControls.Header(Context());

        var ctor = source.IndexOf("public EntryHeader()", StringComparison.Ordinal);
        var height = source.IndexOf("Height = 74;", StringComparison.Ordinal);
        var accent = source.IndexOf("SetAccent(this, BackgroundProperty);", StringComparison.Ordinal);
        var build = source.IndexOf("private void Build()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, ctor);
        Assert.IsGreaterThan(-1, height);
        Assert.IsGreaterThan(-1, accent);
        Assert.IsGreaterThan(-1, build);
        Assert.IsLessThan(build, height, "the invariant chrome moved into Build");
        Assert.IsLessThan(build, accent, "the band's own background moved into Build");
        Assert.IsLessThan(height, ctor, "the chrome is set before the constructor that should hold it");
        Assert.IsLessThan(accent, ctor, "the accent is set before the constructor that should hold it");
    }

    /// <summary>
    /// The record count over the master's list, bound to what the list shows.
    /// </summary>
    /// <remarks>
    /// Neither target had a test naming this, and it showed: WinForms emitted no count at all
    /// while this one emitted a count of the whole store, so the same screen disagreed with itself
    /// across the two front ends and with its own grid whenever the search box had text in it.
    /// Shown, not Count -- MasterScreenTests proves the difference on compiled code.
    /// </remarks>
    [TestMethod]
    public void The_master_list_says_how_many_records_it_is_showing()
        => StringAssert.Contains(
            MasterTemplate(),
            "Text=\"{Binding Shown, StringFormat='Total Records: {0}'}\"");

    // ---- entry-form templates (Task 10) ----

    private static LayoutContext ContextFor(AppScaffolding.Screens.EntryTemplate template)
        => Context(AppCustomization.Default with { EntryTemplate = template });

    [TestMethod]
    public void Classic_lays_out_one_flat_column_with_no_panels()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Classic));

        StringAssert.Contains(source, "FieldGroups.Flat(");
        Assert.DoesNotContain("Panel(group)", source);
    }

    [TestMethod]
    public void TwoColumn_packs_FieldGroups_into_two_columns()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.TwoColumn));

        StringAssert.Contains(source, "FieldGroups.Columns(Record.GetType(), 2)");
    }

    [TestMethod]
    public void Grouped_still_packs_into_the_default_three_columns()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Grouped));

        StringAssert.Contains(source, "FieldGroups.Columns(Record.GetType())");
    }

    [TestMethod]
    public void Tabbed_lays_each_group_out_as_its_own_tab()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Tabbed));

        StringAssert.Contains(source, "new TabControl()");
        StringAssert.Contains(source, "FieldGroups.For(");
        StringAssert.Contains(source, "Header = group.Title");
    }

    [TestMethod]
    public void Every_entry_template_still_compiles_with_FieldGroups_and_RequiredFields()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
        {
            var context = ContextFor(template);

            EmittedDesktopSource.AssertCompilesTogether(
                ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
                ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
                ($"Views/{WpfEntryControls.HeaderFileName}", WpfEntryControls.Header(context)),
                ($"Views/{WpfEntryControls.FormFileName}", WpfEntryControls.Form(context)),
                ($"Views/{WpfEntryControls.NotesFileName}", WpfEntryControls.Notes(context)));
        }
    }

    // ---- ribbon action bar (Task 11) ----

    private static string XamlFor(AppScaffolding.Screens.EntryTemplate template)
        => Xaml(AppCustomization.Default with { EntryTemplate = template });

    /// <summary>As the file's existing <c>TemplateSlice(key)</c>, but against a specific entry
    /// template rather than always the default.</summary>
    private static string TemplateSlice(string key, AppScaffolding.Screens.EntryTemplate template)
    {
        var xaml = XamlFor(template);
        var from = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, from, $"no template keyed '{key}'");

        var depth = 1;
        var at = from;

        while (depth > 0)
        {
            var opened = xaml.IndexOf("<DataTemplate", at + 1, StringComparison.Ordinal);
            var closed = xaml.IndexOf("</DataTemplate>", at + 1, StringComparison.Ordinal);

            Assert.IsGreaterThan(-1, closed, $"'{key}' is never closed");

            if (opened >= 0 && opened < closed)
            {
                depth++;
                at = opened;
                continue;
            }

            depth--;
            at = closed;
        }

        return xaml.Substring(from, at - from);
    }

    [TestMethod]
    public void Ribbon_renders_the_master_action_bar_as_a_compact_strip()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        StringAssert.Contains(master, "Tag=\"ribbon\"");
        StringAssert.Contains(master, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(master, "Command=\"{Binding DeleteCommand}\"");
    }

    [TestMethod]
    public void Non_ribbon_templates_keep_the_plain_button_bar()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.DoesNotContain("Tag=\"ribbon\"", master);
        StringAssert.Contains(master, "<Button Content=\"Save\"");
    }

    [TestMethod]
    public void Ribbon_still_offers_every_command_the_default_bar_offers()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(master, command);
        }
    }

    [TestMethod]
    public void Ribbon_renders_the_editor_action_bar_as_a_compact_strip()
    {
        var editor = TemplateSlice("EditorTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        StringAssert.Contains(editor, "Tag=\"ribbon\"");
        StringAssert.Contains(editor, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(editor, "Command=\"{Binding CancelCommand}\"");
    }

    [TestMethod]
    public void Non_ribbon_templates_keep_the_plain_editor_button_bar()
    {
        var editor = TemplateSlice("EditorTemplate", AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.DoesNotContain("Tag=\"ribbon\"", editor);
        StringAssert.Contains(editor, "<Button Content=\"Save\"");
        StringAssert.Contains(editor, "<Button Content=\"Cancel\"");
    }

    [TestMethod]
    public void Ribbon_renders_the_transaction_action_bar_as_a_compact_strip_but_leaves_the_search_box_alone()
    {
        var transaction = TemplateSlice("TransactionTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        StringAssert.Contains(transaction, "Tag=\"ribbon\"");
        StringAssert.Contains(transaction, "Command=\"{Binding NewCommand}\"");
        StringAssert.Contains(transaction, "Command=\"{Binding OpenCommand}\"");
        StringAssert.Contains(transaction, "Command=\"{Binding DeleteCommand}\"");

        // The search box is untouched: identical in both branches, and never wrapped in a
        // RibbonButton, which only ever wraps a Button.
        StringAssert.Contains(
            transaction,
            "<TextBox Text=\"{Binding Search, UpdateSourceTrigger=PropertyChanged}\"");
    }

    [TestMethod]
    public void Non_ribbon_templates_keep_the_plain_transaction_button_bar()
    {
        var transaction = TemplateSlice("TransactionTemplate", AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.DoesNotContain("Tag=\"ribbon\"", transaction);
        StringAssert.Contains(transaction, "<Button Content=\"New\"");
        StringAssert.Contains(transaction, "<Button Content=\"Open\"");
        StringAssert.Contains(transaction, "<Button Content=\"Delete\"");
        StringAssert.Contains(
            transaction,
            "<TextBox Text=\"{Binding Search, UpdateSourceTrigger=PropertyChanged}\"");
    }

    // ---- Split transaction template (Task 13) ----

    [TestMethod]
    public void Split_gives_the_transaction_screen_an_embedded_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Split);

        var transaction = TemplateSlice("TransactionTemplate", AppScaffolding.Screens.EntryTemplate.Split);

        StringAssert.Contains(transaction, "<views:EntryHeader");
        StringAssert.Contains(transaction, "<views:EntryForm");
        StringAssert.Contains(transaction, "Record=\"{Binding Editor.Draft}\"");
        StringAssert.Contains(transaction, "ItemsSource=\"{Binding Editor.Lines}\"");
        StringAssert.Contains(transaction, "ItemsSource=\"{Binding Editor.Totals}\"");
        StringAssert.Contains(transaction, "<DataGrid");

        // The list is still there, still read-only, still filtered by date -- Split changes
        // composition, not what the list itself offers.
        StringAssert.Contains(transaction, "SelectedDate=\"{Binding From}\"");
    }

    [TestMethod]
    public void Split_removes_the_now_unreachable_standalone_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Split);

        Assert.DoesNotContain("x:Key=\"EditorTemplate\"", xaml);
        Assert.DoesNotContain("Editor=\"{StaticResource EditorTemplate}\"", xaml);
    }

    [TestMethod]
    public void Non_split_templates_keep_the_separate_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Grouped);

        StringAssert.Contains(xaml, "x:Key=\"EditorTemplate\"");
        StringAssert.Contains(xaml, "Editor=\"{StaticResource EditorTemplate}\"");
    }

    [TestMethod]
    public void Split_xaml_is_still_well_formed()
        => Assert.IsNotNull(System.Xml.Linq.XDocument.Parse(XamlFor(AppScaffolding.Screens.EntryTemplate.Split)).Root);

    // ---- full-suite regression proof (Task 14) ----

    [TestMethod]
    public void A_build_naming_no_entry_template_matches_todays_output_byte_for_byte()
    {
        // The regression proof for the whole feature: EffectiveEntryTemplate defaults to Grouped,
        // so Context() (EntryTemplate: null) and Context() with an EXPLICIT Grouped must be
        // byte-identical, and both must equal what this file's pre-existing tests already pin.
        var unset = Xaml();
        var explicitGrouped = XamlFor(AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.AreEqual(unset, explicitGrouped);
    }

    [TestMethod]
    public void Every_entry_templates_window_and_codebehind_compile_together()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
        {
            var context = ContextFor(template);
            var files = new WpfBusinessLayout().ViewFiles(context);

            EmittedDesktopSource.AssertCompilesTogether(
                [
                    .. files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                            .Select(f => (f.Name, f.Source)),
                    ("Views/MainWindow.g.cs", XamlPartialStub),
                    ("Screens/EntityRole.cs", RoleStub),
                    ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
                    ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
                    ("ViewModels/Shell.cs", ShellStub),
                    ("Auth/Session.cs", SessionStub),
                ]);
        }
    }

    [TestMethod]
    public void Every_entry_templates_xaml_is_well_formed()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
            Assert.IsNotNull(System.Xml.Linq.XDocument.Parse(XamlFor(template)).Root);
    }

    /// <summary>
    /// Issue #129: every entry control on both screens is handed the editor's Errors, and each
    /// control marks the fields they name.
    /// </summary>
    [TestMethod]
    public void Every_entry_control_is_bound_to_the_editors_errors()
    {
        var xaml = Xaml();

        Assert.AreEqual(
            System.Text.RegularExpressions.Regex.Count(xaml, "Record=\"\\{Binding (Editor\\.)?Draft\\}\""),
            System.Text.RegularExpressions.Regex.Count(xaml, "Errors=\"\\{Binding (Editor\\.)?Errors\\}\""),
            "an entry control is given a record but not the errors against it");

        var form = WpfEntryControls.Form(Context());
        StringAssert.Contains(form, "nameof(Errors), typeof(IReadOnlyDictionary<string, string>)");
        StringAssert.Contains(form, "Mark(mark.Editor, message);");
        StringAssert.Contains(form, "mark.Message.Text = message;");

        foreach (var source in new[] { WpfEntryControls.Header(Context()), WpfEntryControls.Notes(Context()) })
        {
            StringAssert.Contains(source, "nameof(Errors), typeof(IReadOnlyDictionary<string, string>)");
            StringAssert.Contains(source, "EntryForm.Mark(");
        }
    }
}
