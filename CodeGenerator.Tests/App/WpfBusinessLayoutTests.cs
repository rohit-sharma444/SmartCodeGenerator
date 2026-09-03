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

    /// <summary>The list finds; the editor changes. A typeable grid would put the application back
    /// to editing two different ways.</summary>
    [TestMethod]
    public void The_lists_are_read_only()
    {
        var xaml = Xaml();

        Assert.AreEqual(2, xaml.Split("IsReadOnly=\"True\"").Length - 1);
    }

    [TestMethod]
    public void The_editor_template_offers_save_and_cancel()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding CancelCommand}\"");
    }

    /// <summary>
    /// The third thing a transaction adds: its lines, inside the editor.
    /// </summary>
    /// <remarks>
    /// An ItemsControl over an empty collection renders nothing, which IS the no-lines fallback —
    /// a master's editor is a form and a button row, with no empty grid under it.
    /// </remarks>
    [TestMethod]
    public void The_editor_shows_the_lines_of_a_transaction()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(xaml, "Click=\"OnAddLine\"");
        StringAssert.Contains(xaml, "Click=\"OnRemoveLine\"");

        // The lines grid IS the editor for a line, so unlike the two lists it is typed into.
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding Selected}\"");
        Assert.AreEqual(2, xaml.Split("IsReadOnly=\"True\"").Length - 1);
    }

    [TestMethod]
    public void The_line_buttons_act_on_the_set_they_belong_to()
    {
        var code = new WpfBusinessLayout().ViewFiles(Context())
            .Single(f => f.Name == "MainWindow.xaml.cs").Source;

        StringAssert.Contains(code, "private void OnAddLine(");
        StringAssert.Contains(code, "private void OnRemoveLine(");

        // The DataContext of the button, the same way the rail's buttons carry their screen.
        StringAssert.Contains(code, "DataContext: ILineSet lines");
        StringAssert.Contains(code, "lines.Remove(lines.Selected)");
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
    /// without the parameter name survives a parameter rename; SelectedValuePath is set nowhere
    /// else in the file.
    /// </remarks>
    [TestMethod]
    public void The_form_renders_a_foreign_key_as_a_lookup()
    {
        var source = WpfEntryControls.Form(Context());

        StringAssert.Contains(source, "FieldGroups.NavigationFor(");
        StringAssert.Contains(source, "SelectedValuePath");
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
    /// binding error -- and renders a lookup of bare integers instead of the code a person
    /// recognises. It must read the navigation entity's own code field instead.
    /// </summary>
    [TestMethod]
    public void The_lookup_displays_the_navigation_entitys_code_not_its_id()
        => StringAssert.Contains(
            WpfEntryControls.Form(Context()),
            "DisplayMemberPath = FieldGroups.Key(nav.PropertyType).Code?.Name ?? \"Id\"");

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

    /// <summary>Minor 11. An EntryHeader whose Record is never set must still draw its band: the
    /// invariant chrome (height, background, columns) belongs in the constructor, not only inside
    /// the change callback.</summary>
    [TestMethod]
    public void The_headers_invariant_chrome_is_set_in_the_constructor()
    {
        var source = WpfEntryControls.Header(Context());

        var ctor = source.IndexOf("public EntryHeader()", StringComparison.Ordinal);
        var height = source.IndexOf("Height = 74;", StringComparison.Ordinal);
        var build = source.IndexOf("private void Build()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, ctor);
        Assert.IsGreaterThan(-1, height);
        Assert.IsGreaterThan(-1, build);
        Assert.IsLessThan(build, height, "the invariant chrome moved into Build");
        Assert.IsLessThan(height, ctor, "the chrome is set before the constructor that should hold it");
    }
}
