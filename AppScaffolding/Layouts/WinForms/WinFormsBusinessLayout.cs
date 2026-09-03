using AppScaffolding.Screens;

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

        // The entry screens' accent, the same one the WPF business layout paints the header band
        // and the panel headings with, so the two front ends do not disagree about it. Color.FromArgb
        // rather than ColorTranslator when unthemed, the same reason railFill branches above: an
        // unthemed app's MainForm.cs must contain no "ColorTranslator" at all.
        var accent = context.Palette is null
            ? "Color.FromArgb(0x25, 0x63, 0xEB)"
            : $"ColorTranslator.FromHtml(\"{context.Palette.Accent}\")";

        // The one string that names the long-text tab, read rather than spelled again here: the
        // emitted FieldGroups.AdditionalTitle the WPF layout writes into markup is built from the
        // same constant.
        var additionalTitle = FieldGroupsTemplate.AdditionalTitle;

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
using {{context.RootNamespace}}.Screens;
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
        // The form follows the shell rather than telling it what to show: an editor request comes
        // from a screen, is routed by the shell, and the content area has to notice. One signal
        // instead of a Show() after every action -- and the same signal the WPF binding uses.
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Current))
                Show(_shell.Current);
        };

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
        back.Click += (_, _) => _shell.Back();

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
            if (e.Node?.Tag is { } screen)
                _shell.TryShow(screen);
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

        // A transaction's list stays a list: rewriting BuildList into the entry screen would
        // convert transaction lists too and leave no route to a purchase order's lines.
        if (screen is IEntityViewModel { Role: EntityRole.Transaction })
            return BuildList(screen);

        // A transaction's entry screen is a RecordEditorViewModel<T>, so it arrives here. A
        // master's own screen is never mistaken for it: it implements IDirtyScreen, not this --
        // Task 5 moved IsDirty down to IDirtyScreen for exactly that reason.
        if (screen is IRecordEditor editor)
            return BuildEditor(editor);

        // Everything else is a master: the entry form and the list are one screen.
        return BuildEntry(screen);
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

    /// <summary>
    /// A master's screen: the entry form and the list of what has been entered, one screen.
    /// </summary>
    /// <remarks>
    /// Reached only by the fall-through in <see cref="BuildScreen"/>: a master implements neither
    /// <c>Role.Transaction</c> nor <c>IRecordEditor</c> -- Task 5 moved <c>IsDirty</c> down to
    /// <c>IDirtyScreen</c> for exactly that reason, so a master's own dirty check is never mistaken
    /// for the transaction editor here.
    /// </remarks>
    private Control BuildEntry(object screen)
    {
        var editor = screen.GetType().GetProperty("Editor")?.GetValue(screen);
        var record = editor?.GetType().GetProperty("Draft")?.GetValue(editor);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        layout.Controls.Add(BuildEntryHeader(screen, record), 0, 0);

        if (record is not null)
            layout.Controls.Add(BuildEntryForm(record), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var title = screen.GetType().GetProperty("Title")?.GetValue(screen) as string ?? string.Empty;
        var listPage = new TabPage(title);
        listPage.Controls.Add(BuildRecordList(screen));
        tabs.TabPages.Add(listPage);

        if (record is not null && HasAdditional(screen))
            AddAdditionalTab(tabs, record);

        layout.Controls.Add(tabs, 0, 2);
        layout.Controls.Add(
            BuildEntryActionBar(
                screen,
                ("NewCommand", "New"), ("EditCommand", "Edit"), ("DeleteCommand", "Delete"),
                ("SaveCommand", "Save"), ("ClearCommand", "Clear"), ("CancelCommand", "Cancel")),
            0, 3);

        return layout;
    }

    /// <summary>
    /// The record list inside a master's tab: a search box and the read-only grid.
    /// </summary>
    /// <remarks>
    /// Selecting a row IS opening it -- the master screen loads it into the form itself once
    /// Selected is set, which is why this needs no Open button and no double-click handler, unlike
    /// <see cref="BuildList"/>.
    /// </remarks>
    private Control BuildRecordList(object screen)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var search = new TextBox { Width = 200 };
        search.TextChanged += (_, _) =>
        {
            screen.GetType().GetProperty("Search")?.SetValue(screen, search.Text);
            Refresh(screen, layout);
        };

        layout.Controls.Add(search, 0, 0);

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
            screen.GetType().GetProperty("Selected")?.SetValue(screen, grid.CurrentRow?.DataBoundItem);

        layout.Controls.Add(grid, 0, 1);

        Bind(screen, grid);

        return layout;
    }

    /// <summary>
    /// Whether the owner has long-text fields, and so an Additional Information tab.
    /// </summary>
    /// <remarks>
    /// Read by reflection because neither <c>IEntityViewModel</c> nor <c>IRecordEditor</c> declares
    /// it: a master's screen and a transaction's editor each compute it the same way, from
    /// FieldGroups, but on their own concrete class.
    /// </remarks>
    private static bool HasAdditional(object owner)
        => owner.GetType().GetProperty("HasAdditional")?.GetValue(owner) is true;

    /// <summary>The grouped panels, in the three columns FieldGroups packs them into.</summary>
    private Control BuildEntryForm(object record)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var columns = FieldGroups.Columns(record.GetType());

        for (var i = 0; i < columns.Count; i++)
        {
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Count));

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
            };

            foreach (var group in columns[i])
                stack.Controls.Add(BuildPanel(record, group));

            host.Controls.Add(stack, i, 0);
        }

        return host;
    }

    /// <summary>
    /// One titled panel.
    /// </summary>
    /// <remarks>
    /// A bordered Panel with a Label of its own, not a GroupBox: a GroupBox caption cannot take
    /// the accent colour, which is the whole visual signature of these panels.
    /// </remarks>
    private Control BuildPanel(object record, FieldGroup group)
    {
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(10),
        };

        body.Controls.Add(new Label
        {
            Text = group.Title,
            ForeColor = {{accent}},
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        });

        foreach (var field in group.Fields)
            body.Controls.Add(BuildFieldRow(record, field));

        var panel = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            AutoSize = true,
            Margin = new Padding(6),
        };

        panel.Controls.Add(body);

        return panel;
    }

    /// <summary>A label, an asterisk when the field is required, and the control it is typed
    /// into.</summary>
    private Control BuildFieldRow(object record, PropertyInfo field)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4),
        };

        var required = RequiredFields.IsRequired(record.GetType(), field.Name);

        row.Controls.Add(new Label
        {
            Text = FieldGroups.LabelOf(field),
            AutoSize = false,
            Width = 108,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        row.Controls.Add(new Label
        {
            Text = required ? "*" : string.Empty,
            ForeColor = Color.Red,
            AutoSize = false,
            Width = 12,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        row.Controls.Add(BuildFieldEditor(record, field));

        return row;
    }

    /// <summary>
    /// The control a field is typed into.
    /// </summary>
    /// <remarks>
    /// A foreign key becomes a picker over the entity it points at, the same convention the grids
    /// apply. Rendering it as a text box, or dropping it, would leave no way to set a document's
    /// supplier at all.
    /// </remarks>
    private Control BuildFieldEditor(object record, PropertyInfo field)
    {
        var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;

        if (FieldGroups.NavigationFor(field) is { } nav)
        {
            var lookup = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Id",
                ValueMember = "Id",
                DataSource = LookupRows(nav.PropertyType),
            };

            lookup.DataBindings.Add(
                new Binding("SelectedValue", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return lookup;
        }

        if (underlying == typeof(bool))
        {
            var check = new CheckBox { Width = 190 };

            check.DataBindings.Add(
                new Binding("Checked", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return check;
        }

        if (underlying == typeof(DateTime))
        {
            var picker = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Short };

            picker.DataBindings.Add(
                new Binding("Value", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return picker;
        }

        if (underlying.IsEnum)
        {
            var combo = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DataSource = Enum.GetValues(underlying),
            };

            combo.DataBindings.Add(
                new Binding("SelectedItem", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return combo;
        }

        var box = new TextBox { Width = 190 };

        box.DataBindings.Add(
            new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

        return box;
    }

    /// <summary>
    /// The accent band at the top of an entry screen: a title, a subtitle, and the record's key
    /// fields held out of the panels below.
    /// </summary>
    private Control BuildEntryHeader(object owner, object? record)
    {
        var heading = owner.GetType().GetProperty("Title")?.GetValue(owner) as string ?? string.Empty;

        var band = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = {{accent}} };

        var titles = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Left,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(18, 16, 0, 0),
        };

        titles.Controls.Add(new Label
        {
            Text = heading,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            AutoSize = true,
        });

        var subtitle = heading.Length == 0
            ? string.Empty
            : $"Create and manage {heading.ToLowerInvariant()} records";

        titles.Controls.Add(new Label { Text = subtitle, ForeColor = Color.White, AutoSize = true });

        band.Controls.Add(titles);

        if (record is not null)
            band.Controls.Add(BuildKeyStrip(record));

        return band;
    }

    /// <summary>The record's code, date and status, held out of every panel so they are not drawn
    /// twice on one screen.</summary>
    private Control BuildKeyStrip(object record)
    {
        var strip = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 20, 18, 0),
        };

        var key = FieldGroups.Key(record.GetType());

        foreach (var field in new[] { key.Code, key.Date, key.Status })
        {
            if (field is null)
                continue;

            var cell = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(14, 0, 0, 0),
            };

            cell.Controls.Add(new Label
            {
                Text = FieldGroups.LabelOf(field),
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
            });

            cell.Controls.Add(BuildFieldEditor(record, field));

            strip.Controls.Add(cell);
        }

        return strip;
    }

    /// <summary>The Additional Information tab: the long-text fields, as multi-line boxes.</summary>
    private void AddAdditionalTab(TabControl tabs, object record)
    {
        var fields = FieldGroups.Additional(record.GetType());

        if (fields.Count == 0)
            return;

        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10),
        };

        foreach (var field in fields)
        {
            body.Controls.Add(new Label { Text = FieldGroups.LabelOf(field), AutoSize = true });

            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 520,
                Height = 90,
                Margin = new Padding(0, 0, 0, 10),
            };

            box.DataBindings.Add(
                new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            body.Controls.Add(box);
        }

        var page = new TabPage("{{additionalTitle}}");

        page.Controls.Add(body);
        tabs.TabPages.Add(page);
    }

    /// <summary>The rows a lookup offers, from the pane that holds that entity. Empty when the
    /// application has no pane for it, which is what a line type gets.</summary>
    /// <remarks>
    /// Reads AllRecords rather than Items: Items is the search-filtered list a screen shows, so a
    /// lookup built on it would silently drop every row that does not match text left in that
    /// screen's own search box -- a supplier the user is mid-search on elsewhere vanishing from a
    /// purchase order's supplier picker, with no indication why.
    /// </remarks>
    private IList LookupRows(Type entityType)
    {
        foreach (var group in _shell.Groups)
        {
            foreach (var entity in group.Entities)
            {
                if (!string.Equals(entity.Title, entityType.Name, StringComparison.Ordinal))
                    continue;

                var all = entity.GetType().GetProperty("AllRecords")?.GetValue(entity) as IEnumerable;

                return all?.Cast<object>().ToList() ?? new List<object>();
            }
        }

        return new List<object>();
    }

    /// <summary>The bar every entry screen ends in: a status label and whichever commands the
    /// owner actually has.</summary>
    /// <remarks>
    /// CommandButton resolves its command by name and no-ops when the owner has none, so a
    /// master's six-button bar and a transaction editor's two-button bar can share this rather
    /// than each writing its own click handlers.
    /// </remarks>
    private Control BuildEntryActionBar(object owner, params (string Command, string Text)[] commands)
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var status = new Label { Dock = DockStyle.Fill, Name = "Status" };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };

        foreach (var (command, text) in commands)
            buttons.Controls.Add(CommandButton(owner, command, text));

        bar.Controls.Add(status, 0, 0);
        bar.Controls.Add(buttons, 1, 0);

        return bar;
    }

    /// <summary>
    /// A transaction's entry screen: the same header and form as a master, with its lines and
    /// their totals under it.
    /// </summary>
    /// <remarks>
    /// Reached only through the <c>IRecordEditor</c> test in <see cref="BuildScreen"/>: a master's
    /// inline editor is never shown through here, because a master's own screen implements
    /// <c>IDirtyScreen</c>, not this.
    /// </remarks>
    private Control BuildEditor(IRecordEditor editor)
    {
        var draft = editor.GetType().GetProperty("Draft")?.GetValue(editor);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        layout.Controls.Add(BuildEntryHeader(editor, draft), 0, 0);

        if (draft is not null)
            layout.Controls.Add(BuildEntryForm(draft), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // A row for the lines only when there are lines: a master's own editor has none, and a
        // tab with an empty grid under it would be worse than no tab at all.
        if (editor.Lines.Count > 0)
        {
            var linesPage = new TabPage(editor.Title);

            var linesHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            linesHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            linesHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));

            linesHost.Controls.Add(BuildLines(editor), 0, 0);
            linesHost.Controls.Add(BuildTotals(editor), 1, 0);

            linesPage.Controls.Add(linesHost);
            tabs.TabPages.Add(linesPage);
        }

        if (draft is not null && HasAdditional(editor))
            AddAdditionalTab(tabs, draft);

        layout.Controls.Add(tabs, 0, 2);
        layout.Controls.Add(
            BuildEntryActionBar(editor, ("SaveCommand", "Save"), ("CancelCommand", "Cancel")), 0, 3);

        return layout;
    }

    /// <summary>
    /// The lines' column totals, as right-aligned label pairs, the grand total in bold.
    /// </summary>
    /// <remarks>
    /// Typed against <c>IRecordEditor.Totals</c> directly rather than through <c>dynamic</c>: Task
    /// 7 already put <c>IReadOnlyList&lt;LineTotal&gt; Totals</c> on the interface for exactly this
    /// reason, so there is no open generic here to work around.
    /// </remarks>
    private Control BuildTotals(IRecordEditor editor)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            WrapContents = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10),
        };

        foreach (var total in editor.Totals)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
            };

            row.Controls.Add(new Label { Text = total.Column, Width = 120, AutoSize = false });
            row.Controls.Add(new Label
            {
                Text = total.Value.ToString("N2"),
                Width = 90,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = total.IsGrand ? new Font(Font, FontStyle.Bold) : Font,
            });

            panel.Controls.Add(row);
        }

        return panel;
    }

    /// <summary>
    /// The lines of a transaction, one grid per kind.
    /// </summary>
    /// <remarks>
    /// Bound straight to the set's own collection rather than to a copy of it: this grid IS the
    /// editor for a line, so a cell edit has to reach the object the editor will save. That is what
    /// the BindingList behind ILineSet.Items is for -- a DataGridView needs IBindingList to notice
    /// a row being added.
    /// </remarks>
    private Control BuildLines(IRecordEditor editor)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };

        foreach (var lines in editor.Lines)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            panel.Controls.Add(new Label
            {
                Text = lines.Title,
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            }, 0, 0);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                DataSource = lines.Items,
                AutoGenerateColumns = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };

            grid.SelectionChanged += (_, _) => lines.Selected = grid.CurrentRow?.DataBoundItem;

            // The parent's id is stamped on save, so it is not a field anyone types into.
            grid.DataBindingComplete += (_, _) =>
            {
                if (grid.Columns[lines.ForeignKey] is { } column)
                    column.Visible = false;
            };

            // A typed value reaches the line object directly, so nothing derived from the lines --
            // the totals beside them -- hears about it. Telling the set is what makes that band
            // recompute. Unlike WPF's CellEditEnding, CellEndEdit fires AFTER the grid has pushed
            // the typed value into the bound line, so this needs no dispatcher hop to see it.
            grid.CellEndEdit += (_, _) => lines.LineEdited();

            panel.Controls.Add(grid, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            };

            var remove = new Button { Text = "Remove line", Width = 96 };
            remove.Click += (_, _) => lines.Remove(lines.Selected);

            var add = new Button { Text = "Add line", Width = 88, Margin = new Padding(4, 0, 0, 0) };
            add.Click += (_, _) => lines.Add();

            // No re-bind after Clear, the same as Add and Remove above: the grid's DataSource IS
            // lines.Items, a BindingList<T>, and Clear() raises its ListChanged itself. Rebinding
            // here would replace it with a static snapshot List<object>, and Add/Remove after that
            // would stop reaching the grid at all.
            var clear = new Button { Text = "Clear All", Width = 88 };
            clear.Click += (_, _) => lines.Clear();

            buttons.Controls.Add(remove);
            buttons.Controls.Add(add);
            buttons.Controls.Add(clear);

            panel.Controls.Add(buttons, 0, 2);

            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            host.Controls.Add(panel);
        }

        return host;
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
