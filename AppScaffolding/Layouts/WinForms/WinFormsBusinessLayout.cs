namespace AppScaffolding.Layouts.WinForms;

/// <summary>
/// The business shell in Windows Forms: a grouped tree for the rail, a header naming the signed-in
/// user, and a content panel that swaps one control per kind of screen.
/// </summary>
/// <remarks>
/// The same view models as WPF, with different plumbing — there are no data templates here, so the
/// content panel holds a control and the shell swaps it. A <c>TreeView</c> is the least code that
/// still reads as grouping, which is the same choice the sidebar layout made.
/// <para>
/// The screen controls are built by reflection over the view model rather than typed against it:
/// every screen is <c>MasterScreenViewModel&lt;T&gt;</c> or similar, and a form cannot name an open
/// generic any more than XAML can.
/// </para>
/// </remarks>
public sealed class WinFormsBusinessLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Business);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Business];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs", Source(context)),
    ];

    private static string Source(LayoutContext context)
    {
        var railFill = context.Palette is null
            ? "SystemColors.Control"
            : $"ColorTranslator.FromHtml(\"{context.Palette.Surface}\")";

        // Read off disk beside the binary rather than embedded, as every other layout does:
        // AppScaffolder registers the copy as content copied to the output directory. Guarded, so
        // an asset the user later deletes costs the app its icon rather than its startup.
        var formIcon = context.IconFileName is not null
                       && context.IconFileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? $$"""

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "{{AppCustomization.AssetsFolder}}", "{{context.IconFileName}}");
        if (System.IO.File.Exists(iconPath))
            Icon = new Icon(iconPath);
"""
            : string.Empty;

        return $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using {{context.RootNamespace}}.Auth;
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>The business shell: grouped rail, header, and a content panel one screen deep.</summary>
public sealed class MainForm : Form
{
    private readonly ShellViewModel _shell;
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly TreeView _rail = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BorderStyle = BorderStyle.None,
        BackColor = {{railFill}},
    };

    public MainForm()
    {
        var model = new MainViewModel();

        _shell = new ShellViewModel(
            model.Entities.Where(e => !IsAdminPane(e)).ToList(),
            model.Entities.Where(IsAdminPane).ToList());

        // The desktop's answer to "discard your changes?". The shell only knows it has to ask;
        // what asking looks like belongs to the view.
        _shell.ConfirmDiscard = () => MessageBox.Show(
            this, "Discard your changes?", "Unsaved changes",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

        Text = "{{context.TitleLiteral}}";
        ClientSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
{{formIcon}}
        BuildRail();
        Controls.Add(BuildLayout());

        Show(_shell.Current);
    }

    /// <summary>The Users pane, which belongs under Admin rather than among the entities.</summary>
    private static bool IsAdminPane(IEntityViewModel entity)
        => string.Equals(entity.Title, "Users", StringComparison.Ordinal);

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);
        layout.Controls.Add(_rail, 0, 1);
        layout.Controls.Add(_content, 1, 1);

        return layout;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = {{railFill}} };

        var signOut = new Button { Text = "Sign out", Width = 88, Dock = DockStyle.Right };
        signOut.Click += (_, _) =>
        {
            _shell.SignOutCommand.Execute(null);

            if (Session.SignOutRequested)
                Close();
        };

        var back = new Button { Text = "Back", Width = 72, Dock = DockStyle.Right };
        back.Click += (_, _) => { _shell.Back(); Show(_shell.Current); };

        var who = new Label
        {
            Text = _shell.SignedInUser,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 8, 0),
        };

        var title = new Label
        {
            Text = "{{context.TitleLiteral}}",
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 320,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        };

        header.Controls.Add(who);
        header.Controls.Add(back);
        header.Controls.Add(signOut);
        header.Controls.Add(title);

        return header;
    }

    private void BuildRail()
    {
        var home = new TreeNode("Home") { Tag = _shell.Dashboard };
        _rail.Nodes.Add(home);

        foreach (var group in _shell.Groups)
        {
            var node = new TreeNode(group.Title);

            foreach (var entity in group.Entities)
                node.Nodes.Add(new TreeNode(entity.Title) { Tag = entity });

            _rail.Nodes.Add(node);
            node.Expand();
        }

        // A group heading is not a screen, so clicking one navigates nowhere.
        _rail.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is { } screen && _shell.TryShow(screen))
                Show(_shell.Current);
        };
    }

    /// <summary>Swaps the content panel to whatever the shell is showing.</summary>
    private void Show(object? screen)
    {
        _content.Controls.Clear();

        if (screen is null)
            return;

        _content.Controls.Add(BuildScreen(screen));
    }

    /// <summary>
    /// One control per kind of screen.
    /// </summary>
    /// <remarks>
    /// Reflection rather than a typed cast: every screen is a closed generic whose argument is one
    /// of the discovered models, and this file cannot name them.
    /// </remarks>
    private Control BuildScreen(object screen)
    {
        if (screen is DashboardViewModel dashboard)
            return BuildDashboard(dashboard);

        if (screen is IRecordEditor editor)
            return BuildEditor(editor);

        return BuildList(screen);
    }

    private Control BuildDashboard(DashboardViewModel dashboard)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };

        foreach (var group in dashboard.Groups)
        {
            var heading = new Label
            {
                Text = group.Title,
                AutoSize = false,
                Width = 900,
                Height = 24,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            };

            flow.Controls.Add(heading);
            flow.SetFlowBreak(heading, true);

            foreach (var entity in group.Entities)
            {
                var card = new Panel
                {
                    Width = 150, Height = 70, BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 0, 10, 10),
                };

                card.Controls.Add(new Label
                {
                    Text = entity.Count.ToString(),
                    Dock = DockStyle.Top,
                    Height = 34,
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 16f, FontStyle.Bold),
                });

                card.Controls.Add(new Label { Text = entity.Title, Dock = DockStyle.Bottom, Height = 20 });

                flow.Controls.Add(card);
            }

            flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);
        }

        return flow;
    }

    private Control BuildEditor(IRecordEditor editor)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        layout.Controls.Add(new Label
        {
            Text = editor.Title,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        }, 0, 0);

        // A PropertyGrid over the DRAFT, so every field is editable without this file naming one.
        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = editor.GetType().GetProperty("Draft")?.GetValue(editor),
            ToolbarVisible = false,
        };

        layout.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };

        buttons.Controls.Add(CommandButton(editor, "SaveCommand", "Save"));
        buttons.Controls.Add(CommandButton(editor, "CancelCommand", "Cancel"));

        layout.Controls.Add(buttons, 0, 2);

        return layout;
    }

    private Control BuildList(object screen)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };

        toolbar.Controls.Add(CommandButton(screen, "DeleteCommand", "Delete"));
        toolbar.Controls.Add(CommandButton(screen, "OpenCommand", "Open"));
        toolbar.Controls.Add(CommandButton(screen, "NewCommand", "New"));

        var search = new TextBox { Width = 160 };
        search.TextChanged += (_, _) =>
        {
            screen.GetType().GetProperty("Search")?.SetValue(screen, search.Text);
            Refresh(screen, layout);
        };

        toolbar.Controls.Add(search);
        layout.Controls.Add(toolbar, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Name = "Grid",
        };

        grid.SelectionChanged += (_, _) =>
            screen.GetType().GetProperty("Selected")?
                .SetValue(screen, grid.CurrentRow?.DataBoundItem);

        // Double-click opens, the same thing the Open button does.
        grid.CellDoubleClick += (_, _) => Invoke(screen, "OpenCommand");

        layout.Controls.Add(grid, 0, 1);

        var status = new Label { Dock = DockStyle.Fill, Name = "Status" };
        layout.Controls.Add(status, 0, 2);

        Bind(screen, grid);
        SubscribeToEditor(screen);

        return layout;
    }

    private void Refresh(object screen, Control host)
    {
        if (host.Controls.Find("Grid", true).FirstOrDefault() is DataGridView grid)
            Bind(screen, grid);
    }

    private static void Bind(object screen, DataGridView grid)
    {
        var items = screen.GetType().GetProperty("Items")?.GetValue(screen) as IEnumerable;

        grid.DataSource = items?.Cast<object>().ToList();
    }

    /// <summary>Wires the screen's editor request to the shell, which is what opens it.</summary>
    private void SubscribeToEditor(object screen)
    {
        var evt = screen.GetType().GetEvent("EditorRequested");

        if (evt is null)
            return;

        var handler = GetType()
            .GetMethod(nameof(OnEditorRequested), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(evt.EventHandlerType!.GetGenericArguments()[0]);

        evt.AddEventHandler(screen, Delegate.CreateDelegate(evt.EventHandlerType!, this, handler));
    }

    private void OnEditorRequested<TEditor>(object? sender, TEditor editor)
    {
        if (editor is null)
            return;

        var opened = sender;

        if (editor is IRecordEditor && _shell.TryShow(editor))
        {
            Show(_shell.Current);

            // When the editor finishes, the list underneath reloads and comes back.
            editor.GetType().GetEvent("Closed")?.AddEventHandler(
                editor,
                new EventHandler((_, _) =>
                {
                    opened?.GetType().GetMethod("Reload")?.Invoke(opened, null);
                    _shell.Back();
                    Show(_shell.Current);
                }));
        }
    }

    private Button CommandButton(object owner, string commandName, string text)
    {
        var button = new Button { Text = text, Width = 80, Margin = new Padding(4, 0, 0, 0) };

        button.Click += (_, _) => Invoke(owner, commandName);

        return button;
    }

    private static void Invoke(object owner, string commandName)
    {
        if (owner.GetType().GetProperty(commandName)?.GetValue(owner) is System.Windows.Input.ICommand command
            && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
""";
    }
}
