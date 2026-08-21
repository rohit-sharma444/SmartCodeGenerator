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

    [TestMethod]
    public void The_editor_edits_the_draft_and_offers_save_and_cancel()
    {
        var source = Source();

        // The PropertyGrid binds the DRAFT, not the record: the copy is what makes Cancel mean
        // something, and binding the record would edit it live behind the editor.
        StringAssert.Contains(source, "GetProperty(\"Draft\")");
        StringAssert.Contains(source, "CommandButton(editor, \"SaveCommand\", \"Save\")");
        StringAssert.Contains(source, "CommandButton(editor, \"CancelCommand\", \"Cancel\")");
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

    [TestMethod]
    public void The_form_compiles_against_the_screens_it_binds_to()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("Screens/EntityRole.cs", RoleStub),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    [TestMethod]
    public void The_themed_form_compiles_too()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source(AppCustomization.Default with { Theme = AppTheme.Dark })),
            ("Screens/EntityRole.cs", RoleStub),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    private const string RoleStub = """
        namespace Payroll.WinForms.Screens;

        public enum EntityRole { Master, Transaction, DocumentLine }
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

        public interface IRecordEditor
        {
            bool IsDirty { get; }
            string Title { get; }
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
