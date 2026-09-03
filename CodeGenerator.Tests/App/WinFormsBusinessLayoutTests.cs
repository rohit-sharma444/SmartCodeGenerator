using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The business shell in Windows Forms: a grouped tree rail, a header, and one content panel.
/// </summary>
/// <remarks>
/// The Windows Forms half of <see cref="WpfBusinessLayoutTests"/>. It shipped without these, which
/// is how 400 lines of emitted form came to have no assertion over them at all.
/// </remarks>
[TestClass]
public class WinFormsBusinessLayoutTests
{
    private const string Root = "Payroll.WinForms";

    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            Root, "Payroll", settings, ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    private static string Source(AppCustomization? customization = null)
        => new WinFormsBusinessLayout().ViewFiles(Context(customization))
            .Single(f => f.Name == "MainForm.cs").Source;

    [TestMethod]
    public void The_registry_resolves_the_business_key()
    {
        Assert.AreEqual("business", WinFormsLayouts.Resolve(AppLayout.Business).Key);
        Assert.AreEqual(
            AppLayoutCatalog.Descriptions[AppLayout.Business],
            WinFormsLayouts.Resolve(AppLayout.Business).Description);
    }

    /// <summary>The registries fall back to tabs for an unimplemented layout, which would make a
    /// missing layout look like a working app rather than a failure.</summary>
    [TestMethod]
    public void Every_layout_value_has_a_windows_forms_layout()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.AreEqual(
                AppLayoutCatalog.KeyFor(layout), WinFormsLayouts.Resolve(layout).Key, $"{layout} falls back");
        }
    }

    [TestMethod]
    public void The_rail_is_a_tree_with_a_node_per_group_and_a_leaf_per_entity()
    {
        var source = Source();

        StringAssert.Contains(source, "TreeView _rail = new()");
        StringAssert.Contains(source, "foreach (var group in _shell.Groups)");
        StringAssert.Contains(source, "new TreeNode(group.Title)");
        StringAssert.Contains(source, "node.Nodes.Add(new TreeNode(entity.Title)");

        // Home is a node too, or the dashboard is reachable only at startup.
        StringAssert.Contains(source, "new TreeNode(\"Home\")");
    }

    /// <summary>A group heading is not a screen. Tagging one would navigate the content area to a
    /// NavGroup and render its type name.</summary>
    [TestMethod]
    public void A_group_heading_navigates_nowhere()
    {
        StringAssert.Contains(Source(), "if (e.Node?.Tag is { } screen)");
        StringAssert.Contains(Source(), "_shell.TryShow(screen);");
    }

    /// <summary>
    /// The form follows the shell rather than telling it what to show.
    /// </summary>
    /// <remarks>
    /// An editor request comes from a screen and is routed by the shell, so the content area has
    /// to notice a change it did not cause. Pushing from each click instead left New doing nothing
    /// once the routing moved into the shell — one signal, and both targets use the same one.
    /// </remarks>
    [TestMethod]
    public void The_content_area_follows_the_shell()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.PropertyChanged +=");
        StringAssert.Contains(source, "e.PropertyName == nameof(ShellViewModel.Current)");
    }

    [TestMethod]
    public void The_header_names_the_signed_in_user_and_offers_sign_out_and_back()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.SignedInUser");
        StringAssert.Contains(source, "Text = \"Sign out\"");
        StringAssert.Contains(source, "_shell.SignOutCommand.Execute(null)");
        StringAssert.Contains(source, "Text = \"Back\"");
        StringAssert.Contains(source, "_shell.Back()");
    }

    /// <summary>One content panel, cleared and refilled — not a panel per screen left hidden
    /// behind the others.</summary>
    [TestMethod]
    public void One_content_panel_holds_whichever_screen_is_showing()
    {
        var source = Source();

        StringAssert.Contains(source, "_content.Controls.Clear();");
        StringAssert.Contains(source, "_content.Controls.Add(BuildScreen(screen));");
        StringAssert.Contains(source, "Show(_shell.Current)");
    }

    /// <summary>The shell only knows it has to ask; what asking looks like is the view's, and on
    /// this target it is a message box.</summary>
    [TestMethod]
    public void The_discard_prompt_is_a_message_box_wired_into_the_shell()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.ConfirmDiscard = () => MessageBox.Show(");
        StringAssert.Contains(source, "\"Discard your changes?\"");
        StringAssert.Contains(source, "== DialogResult.Yes");
    }

    /// <summary>Three screens, chosen by what the value IS rather than by its type name: every
    /// screen here is a closed generic this file cannot name.</summary>
    [TestMethod]
    public void The_screens_are_chosen_by_the_faces_they_implement()
    {
        var source = Source();

        StringAssert.Contains(source, "if (screen is DashboardViewModel dashboard)");
        StringAssert.Contains(source, "if (screen is IRecordEditor editor)");
        StringAssert.Contains(source, "return BuildList(screen);");
    }

    /// <summary>
    /// A transaction's LIST stays on BuildList. This is the whole reason the three-way dispatch
    /// exists: rewriting BuildList into the entry screen would convert transaction lists too and
    /// leave no route to a purchase order's lines.
    /// </summary>
    [TestMethod]
    public void A_transaction_list_is_routed_to_BuildList()
    {
        var source = Source();

        var transaction = source.IndexOf(
            "if (screen is IEntityViewModel { Role: EntityRole.Transaction })", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, transaction, "no Role.Transaction test in the dispatch");

        var recordEditor = source.IndexOf("if (screen is IRecordEditor", transaction, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, recordEditor, "no IRecordEditor branch after the transaction test");

        // The transaction branch's own return, before the next branch begins.
        StringAssert.Contains(
            source.Substring(transaction, recordEditor - transaction), "return BuildList(screen);");
    }

    /// <summary>A transaction's stand-alone entry screen -- a RecordEditorViewModel -- is routed
    /// to BuildEditor.</summary>
    [TestMethod]
    public void A_transaction_editor_is_routed_to_BuildEditor()
    {
        var source = Source();

        var branch = source.IndexOf("if (screen is IRecordEditor editor)", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, branch, "no IRecordEditor test in the dispatch");

        StringAssert.Contains(
            source.Substring(branch, Math.Min(160, source.Length - branch)), "return BuildEditor(editor);");
    }

    /// <summary>
    /// A master's own screen implements neither Role.Transaction nor IRecordEditor, so it falls
    /// through to the new BuildEntry -- the opposite of routing the entry screen off
    /// Role.Transaction, which would send a TRANSACTION's list to the entry screen instead of a
    /// master's.
    /// </summary>
    [TestMethod]
    public void A_master_screen_falls_through_to_BuildEntry()
    {
        var source = Source();

        var branch = source.IndexOf("if (screen is IRecordEditor editor)", StringComparison.Ordinal);
        var editorReturn = source.IndexOf("return BuildEditor(editor);", branch, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, editorReturn, "no BuildEditor return after the IRecordEditor test");

        var tail = source.Substring(editorReturn);
        StringAssert.Contains(tail, "return BuildEntry(screen);");
        Assert.DoesNotContain("return BuildList(screen);", tail);
    }

    /// <summary>The list finds; the editor changes. A typeable grid would put the application back
    /// to editing two different ways.</summary>
    [TestMethod]
    public void The_list_is_read_only_and_opens_the_editor()
    {
        var source = Source();

        StringAssert.Contains(source, "ReadOnly = true,");
        StringAssert.Contains(source, "AllowUserToAddRows = false,");
        StringAssert.Contains(source, "CommandButton(screen, \"OpenCommand\", \"Open\")");
        StringAssert.Contains(source, "grid.CellDoubleClick");
    }

    /// <summary>
    /// The lines of a transaction, in a grid inside the editor.
    /// </summary>
    /// <remarks>
    /// Bound to the set's own collection rather than a copy: this grid IS the editor for a line, so
    /// a cell edit has to reach the object the editor will save.
    /// </remarks>
    [TestMethod]
    public void The_editor_shows_the_lines_of_a_transaction()
    {
        var source = Source();

        StringAssert.Contains(source, "foreach (var lines in editor.Lines)");
        StringAssert.Contains(source, "DataSource = lines.Items,");
        StringAssert.Contains(source, "lines.Add()");
        StringAssert.Contains(source, "lines.Remove(lines.Selected)");

        // The parent's id is stamped on save, so it is not a column anyone types into.
        StringAssert.Contains(source, "grid.Columns[lines.ForeignKey]");
    }

    /// <summary>A master's own editor has no lines, and a tab with an empty grid under it would be
    /// worse than no tab at all -- so the lines tab is added only when there are some.</summary>
    [TestMethod]
    public void An_editor_with_no_lines_adds_no_lines_tab()
        => StringAssert.Contains(Source(), "if (editor.Lines.Count > 0)");

    [TestMethod]
    public void The_form_builds_a_header_a_grouped_form_and_a_tab_strip()
    {
        var source = Source();

        StringAssert.Contains(source, "private Control BuildEntryHeader(");
        StringAssert.Contains(source, "private Control BuildEntryForm(");
        StringAssert.Contains(source, "new TabControl");
        StringAssert.Contains(source, "FieldGroups.Columns(");
    }

    /// <summary>A GroupBox caption cannot take the accent colour, so the panel is a bordered Panel
    /// with a Label of its own.</summary>
    [TestMethod]
    public void A_panel_is_not_a_group_box()
        => Assert.DoesNotContain("new GroupBox", Source());

    [TestMethod]
    public void The_form_marks_required_fields_and_renders_lookups()
    {
        var source = Source();

        StringAssert.Contains(source, "RequiredFields.IsRequired(");
        StringAssert.Contains(source, "FieldGroups.NavigationFor(");
    }

    [TestMethod]
    public void The_action_bar_offers_only_commands_that_exist()
    {
        var source = Source();

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(source, command);
        }

        foreach (var absent in new[] { "\"Print\"", "\"Export\"", "\"Import Items\"" })
            Assert.DoesNotContain(absent, source);
    }

    [TestMethod]
    public void The_editor_shows_the_line_totals_and_can_clear_a_set()
    {
        var source = Source();

        StringAssert.Contains(source, "Totals");
        StringAssert.Contains(source, "\"Clear All\"");
    }

    /// <summary>
    /// R42: every front end owes ILineSet.LineEdited() on a committed cell edit, or the totals
    /// band beside the grid goes stale the moment someone types a new quantity into a line.
    /// </summary>
    /// <remarks>
    /// CellEndEdit, not CellValueChanged: WPF needed a Dispatcher hop because its CellEditEnding
    /// fires BEFORE the grid pushes the typed value into the bound object, but WinForms'
    /// CellEndEdit fires AFTER the push, so the call can go straight in the handler.
    /// </remarks>
    [TestMethod]
    public void A_committed_line_edit_is_reported_to_the_set()
        => StringAssert.Contains(Source(), "grid.CellEndEdit += (_, _) => lines.LineEdited();");

    /// <summary>Behind HasAdditional on both screens, so a model with no long-text fields shows no
    /// empty tab -- the same rule that drops an empty panel.</summary>
    [TestMethod]
    public void The_additional_fields_are_shown_only_when_there_are_some()
    {
        var source = Source();

        StringAssert.Contains(source, "HasAdditional");
        StringAssert.Contains(source, "FieldGroups.Additional(");
        StringAssert.Contains(source, "\"Additional Information\"");
    }

    [TestMethod]
    public void An_unthemed_shell_uses_the_system_colours()
    {
        StringAssert.Contains(Source(), "BackColor = SystemColors.Control");
    }

    [TestMethod]
    public void A_themed_shell_paints_the_rail_from_the_palette()
    {
        var palette = ThemePalette.Resolve(AppTheme.Dark, accent: null)!;
        var themed = Source(AppCustomization.Default with { Theme = AppTheme.Dark });

        StringAssert.Contains(themed, $"ColorTranslator.FromHtml(\"{palette.Surface}\")");
        Assert.DoesNotContain("SystemColors.Control", themed);
    }

    [TestMethod]
    public void The_title_is_escaped_for_a_source_literal_not_for_markup()
    {
        var source = Source(AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        });

        StringAssert.Contains(source, "Smith & Sons");
        Assert.DoesNotContain("&amp;", source);
    }

    /// <summary>
    /// The whole point of three templates rather than a view per entity: the form is the same file
    /// for a model of two entities and a model of twenty.
    /// </summary>
    /// <remarks>
    /// Generated through the target rather than the layout, because the layout is never handed
    /// entity names — so asserting a name's absence against it could not fail.
    /// </remarks>
    [TestMethod]
    public void The_form_does_not_grow_with_the_model()
    {
        Assert.AreEqual(MainForm(["Order", "Customer"]), MainForm(Twenty()));

        static string MainForm(IReadOnlyList<string> names)
            => new WinFormsTarget().FrontEndFiles(
                    new FrontEndContext(
                        "Payroll", names,
                        AppCustomization.Default with { Layout = AppLayout.Business },
                        EntityRoleMap.Empty))
                .Single(f => f.Name == "MainForm.cs").Source;

        static IReadOnlyList<string> Twenty()
            => [.. Enumerable.Range(1, 20).Select(i => $"Entity{i}")];
    }

    /// <summary>
    /// The emitted form compiles against the emitted screens it binds to.
    /// </summary>
    /// <remarks>
    /// FieldGroups and RequiredFields travel with it now: the entry screens reflect over both, so
    /// a compile test that omitted them would no longer say anything true about the file it claims
    /// to prove compiles.
    /// </remarks>
    [TestMethod]
    public void The_form_compiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("Screens/EntityRole.cs", RoleStub),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source(Root)),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source(Root, null)),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    [TestMethod]
    public void The_themed_form_compiles_too()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source(AppCustomization.Default with { Theme = AppTheme.Dark })),
            ("Screens/EntityRole.cs", RoleStub),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source(Root)),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source(Root, null)),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    private const string RoleStub = """
        namespace Payroll.WinForms.Screens;

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

    /// <summary>
    /// Just enough of the screens for the form to bind to.
    /// </summary>
    /// <remarks>
    /// Stubs rather than the real emitted view models, which pull in the store, the models and the
    /// validators: a compile test of a VIEW should fail for reasons in the view.
    /// </remarks>
    private const string ShellStub = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Windows.Input;
        using Payroll.WinForms.Screens;

        namespace Payroll.WinForms.ViewModels;

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

        public sealed record NavGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

        public sealed record DashboardGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

        public sealed class DashboardViewModel
        {
            public IReadOnlyList<DashboardGroup> Groups { get; } = new List<DashboardGroup>();
        }

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();
        }

        public sealed class ShellViewModel : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            public ShellViewModel(
                IReadOnlyList<IEntityViewModel> entities, IReadOnlyList<IEntityViewModel> adminEntities)
            {
            }

            public Func<bool> ConfirmDiscard { get; set; } = () => true;
            public DashboardViewModel Dashboard { get; } = new();
            public IReadOnlyList<NavGroup> Groups { get; } = new List<NavGroup>();
            public string SignedInUser => string.Empty;
            public object? Current => Dashboard;
            public ICommand SignOutCommand { get; } = null!;
            public bool TryShow(object screen) => true;
            public void Back() { }
        }
        """;

    private const string SessionStub = """
        namespace Payroll.WinForms.Auth;

        public static class Session
        {
            public static bool SignOutRequested { get; private set; }
            public static void RequestSignOut() => SignOutRequested = true;
        }
        """;
}
