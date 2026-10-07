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

        // As the four older layouts do: a nologin app emits no Auth folder, so naming it is
        // CS0234, and there is nobody to sign out -- nor a SignOutCommand on the shell to call.
        var authUsing = context.Customization.Login
            ? $"using {context.RootNamespace}.Auth;\r\n"
            : string.Empty;

        var signOutButton = context.Customization.Login
            ? """
        var signOut = new Button { Text = "Sign out", Width = 88, Dock = DockStyle.Right };
        signOut.Click += (_, _) =>
        {
            _shell.SignOutCommand.Execute(null);

            if (Session.SignOutRequested)
                Close();
        };


"""
            : string.Empty;

        var addSignOut = context.Customization.Login
            ? "        header.Controls.Add(signOut);\r\n"
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
{{authUsing}}using {{context.RootNamespace}}.Screens;
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>The business shell: grouped rail, header, and a content panel one screen deep.</summary>
public sealed class MainForm : Form
{
    private readonly ShellViewModel _shell;
    private readonly Panel _content = new() { Dock = DockStyle.Fill };

    /// <summary>The icon beside each field the last refused save named, its message on hover. One
    /// for the whole window: a control that is disposed takes its error with it.</summary>
    private readonly ErrorProvider _fieldErrors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
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

{{signOutButton}}        var back = new Button { Text = "Back", Width = 72, Dock = DockStyle.Right };
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
{{addSignOut}}        header.Controls.Add(title);

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
    /// <remarks>
    /// Disposing the discarded tree, not just clearing it: Control.Dispose cascades to every child,
    /// which is also what fires each control's own Disposed event -- the signal BuildEntry and
    /// BuildEditor use to unsubscribe the PropertyChanged handler they attach to the screen, so a
    /// rail click does not leak a handler onto a screen this form no longer shows.
    /// <para>
    /// Snapshotted with ToArray first, because Control.Dispose takes the control out of this very
    /// collection. ControlCollection's enumerator has no version check, so a foreach that disposes
    /// as it walks _content.Controls does NOT throw: the controls behind the disposed one shift
    /// down into an index the enumerator has already passed, so it silently disposes only every
    /// second one -- measured on a real collection: three controls left one alive, fifty left
    /// twenty-five. Half the discarded tree would stay alive with its DataBindings still registered
    /// in this form's BindingContext and its screen subscription still attached, looking for all
    /// the world like it had been cleaned up. Today Show clears and adds exactly one control, so
    /// the count never exceeds one and every-second-one disposes correctly either way: the
    /// snapshot is what keeps that true the day this panel holds two.
    /// </para>
    /// </remarks>
    private void Show(object? screen)
    {
        foreach (var discarded in _content.Controls.Cast<Control>().ToArray())
            discarded.Dispose();

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
    /// <para>
    /// A master's <c>Editor</c> is REPLACED, not mutated, every time a row is selected, New/Edit is
    /// clicked, Clear is clicked, or Save succeeds (<c>MasterScreenTemplate.cs</c>: the
    /// <c>Selected</c> setter, <c>Edit</c>, <c>Clear</c>, and after <c>Save</c>). Reading
    /// <c>screen.Editor.Draft</c> once and handing it to every control's <c>DataBindings</c> would
    /// leave the form on screen bound to the ORIGINAL draft forever: selecting a row would not load
    /// it, and Save would write back whatever the blank draft happened to hold while the model
    /// believed a different record was open. So the header and the form are rebuilt from scratch on
    /// every <c>"Editor"</c> change, the same way WPF's <c>Record="{Binding Editor.Draft}"</c>
    /// re-resolves when <c>Editor</c> raises <c>PropertyChanged</c>.
    /// </para>
    /// </remarks>
    private Control BuildEntry(object screen)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, AutoScroll = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        KeepTabStripOnScreen(layout);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var title = screen.GetType().GetProperty("Title")?.GetValue(screen) as string ?? string.Empty;
        var listPage = new TabPage(title);

        var (listControl, refreshList) = BuildRecordList(screen);
        listPage.Controls.Add(listControl);
        tabs.TabPages.Add(listPage);

        Control? header = null;
        Control? form = null;
        TabPage? additionalPage = null;

        // The editor whose Errors the form is showing. A refused save keeps the same editor and
        // announces its Errors; it is the SCREEN that announces a new editor, so the form has to
        // listen to both, and let go of the old editor each time the screen replaces it.
        INotifyPropertyChanged? watched = null;

        void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Errors")
                PaintErrors(sender, header, form, additionalPage);
        }

        // Repaints everything bound to the CURRENT draft. Called once for the initial paint and
        // again every time the screen replaces its Editor -- see the remarks above.
        void RepaintRecord()
        {
            var editor = screen.GetType().GetProperty("Editor")?.GetValue(screen);
            var record = editor?.GetType().GetProperty("Draft")?.GetValue(editor);

            if (watched is not null)
                watched.PropertyChanged -= OnEditorChanged;

            watched = editor as INotifyPropertyChanged;

            if (watched is not null)
                watched.PropertyChanged += OnEditorChanged;

            if (header is not null)
            {
                layout.Controls.Remove(header);
                header.Dispose();
            }

            if (form is not null)
            {
                layout.Controls.Remove(form);
                form.Dispose();
            }

            if (additionalPage is not null)
            {
                tabs.TabPages.Remove(additionalPage);
                additionalPage.Dispose();
                additionalPage = null;
            }

            header = BuildEntryHeader(screen, record);
            layout.Controls.Add(header, 0, 0);

            form = record is not null ? BuildEntryForm(record) : null;

            if (form is not null)
                layout.Controls.Add(form, 0, 1);

            if (record is not null && HasAdditional(screen))
                additionalPage = AddAdditionalTab(tabs, record);

            PaintErrors(editor, header, form, additionalPage);
        }

        RepaintRecord();

        layout.Disposed += (_, _) =>
        {
            if (watched is not null)
                watched.PropertyChanged -= OnEditorChanged;
        };

        layout.Controls.Add(tabs, 0, 2);

        var bar = BuildEntryActionBar(
            screen,
            ("NewCommand", "New"), ("EditCommand", "Edit"), ("DeleteCommand", "Delete"),
            ("SaveCommand", "Save"), ("ClearCommand", "Clear"), ("CancelCommand", "Cancel"));

        layout.Controls.Add(bar, 0, 3);

        // Painted once here as well as on every change: a screen the rail comes back to is rebuilt
        // from scratch, and whatever it last reported is still its Status.
        ShowStatus(bar, screen);

        // Three things this screen shows that a one-shot snapshot cannot: the form re-pointed at a
        // freshly opened record, the list re-read after a save or delete on THIS screen (unlike the
        // old flat editor, saving no longer happens on a different screen that re-runs Show), and
        // the status line a master's own commands write to.
        if (screen is INotifyPropertyChanged notify)
        {
            PropertyChangedEventHandler handler = (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case "Editor":
                        RepaintRecord();
                        break;

                    case "Items":
                        refreshList();
                        break;

                    case "Status":
                        ShowStatus(bar, screen);
                        break;
                }
            };

            notify.PropertyChanged += handler;

            // Unsubscribed when Show discards this screen's control tree, or every rail click
            // leaves another handler on the screen this form no longer shows.
            layout.Disposed += (_, _) => notify.PropertyChanged -= handler;
        }

        return layout;
    }

    /// <summary>
    /// The record list inside a master's tab: a search box and the read-only grid.
    /// </summary>
    /// <remarks>
    /// Selecting a row IS opening it -- the master screen loads it into the form itself once
    /// Selected is set, which is why this needs no Open button and no double-click handler, unlike
    /// <see cref="BuildList"/>. That is also the trap: <c>DataGridView.DataSource</c> auto-selects
    /// a row the moment it is set, so every rebind -- the initial paint, a keystroke in search, a
    /// refresh after Save -- would otherwise fire <c>SelectionChanged</c> and silently replace
    /// whatever the user has open in the form above with row 0. The suppress flag is what tells a
    /// rebind apart from an actual click, and the returned <c>Rebind</c> action is what lets
    /// <see cref="BuildEntry"/> drive the same guarded rebind when the screen announces a fresh
    /// <c>Items</c> list.
    /// </remarks>
    private (Control Control, Action Rebind) BuildRecordList(object screen)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

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

        // The record count the spec puts beside the search box. WPF bound one and this target
        // emitted none at all, so the same screen counted its records on one front end and not the
        // other -- and nothing failed, because no test named it on either.
        var count = new Label
        {
            Dock = DockStyle.Right,
            Width = 160,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Name = "RecordCount",
        };

        var suppressSelection = false;

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelection)
                return;

            screen.GetType().GetProperty("Selected")?.SetValue(screen, grid.CurrentRow?.DataBoundItem);
        };

        void Rebind()
        {
            suppressSelection = true;

            try
            {
                Bind(screen, grid);
            }
            finally
            {
                suppressSelection = false;
            }

            // Shown, not Count: Count is every row the store holds, and this label sits directly
            // above a grid the search box narrows.
            var shown = screen.GetType().GetProperty("Shown")?.GetValue(screen);
            count.Text = shown is null ? string.Empty : $"Total Records: {shown}";
        }

        var search = new TextBox { Width = 200, Dock = DockStyle.Left };

        var searchRow = new Panel { Dock = DockStyle.Fill };
        searchRow.Controls.Add(count);
        searchRow.Controls.Add(search);

        // Rebound here rather than left to the screen's own Items notification: this file reaches
        // Search by reflection, so it cannot assume the screen announces anything at all. A screen
        // that does announce it simply rebinds twice, which costs a list copy and nothing else.
        search.TextChanged += (_, _) =>
        {
            screen.GetType().GetProperty("Search")?.SetValue(screen, search.Text);
            Rebind();
        };

        layout.Controls.Add(searchRow, 0, 0);
        layout.Controls.Add(grid, 0, 1);

        Rebind();

        return (layout, Rebind);
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

    /// <summary>
    /// The grouped panels, in the columns FieldGroups packs them into, side by side.
    /// </summary>
    /// <remarks>
    /// A wrapping flow rather than a TableLayoutPanel of percentage columns. An AutoSize table
    /// sizes each 33% column so its content fits, so it reported about three times its widest
    /// column as its width, and the scrolling page opened scrolled sideways, past the form, at
    /// the default window size. A flow is exactly as wide as the columns it holds, and moves a
    /// column underneath only when the window is too narrow for it. An empty column adds nothing.
    /// </remarks>
    private Control BuildEntryForm(object record)
    {
        var host = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var columns = FieldGroups.Columns(record.GetType());

        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Count == 0)
                continue;

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
            };

            foreach (var group in columns[i])
                stack.Controls.Add(BuildPanel(record, group));

            host.Controls.Add(stack);
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
        var editor = CreateFieldEditor(record, field);

        // The property it edits, which is how PaintErrors finds the control a refusal names; and
        // room on the right for the error icon, which is drawn beside the control, not inside it.
        editor.Tag = field;
        editor.Margin = new Padding(editor.Margin.Left, editor.Margin.Top, 22, editor.Margin.Bottom);

        return editor;
    }

    private Control CreateFieldEditor(object record, PropertyInfo field)
    {
        var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;

        if (FieldGroups.NavigationFor(field) is { } nav)
        {
            // DisplayMember is what a person calls the row, not its key: listing suppliers as
            // 1, 2, 3 is no way to choose one, and the mockup picks a supplier by name.
            // FieldGroups.LookupLabel makes that choice once, for this target and for WPF.
            var lookup = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = FieldGroups.LookupLabel(nav.PropertyType),
                ValueMember = "Id",
                DataSource = LookupRows(nav.PropertyType),
            };

            lookup.DataBindings.Add(
                new Binding("SelectedValue", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return lookup;
        }

        // A nullable flag or date (bool?, DateTime?) is one whose source rows had gaps. Neither a
        // CheckBox's Checked nor a DateTimePicker's Value can hold "none": bound directly, a
        // missing date showed TODAY, which reads as real data. So both are synced by hand, the
        // flag as a three-state box (indeterminate = none) and the date with a tick box.
        var nullable = Nullable.GetUnderlyingType(field.PropertyType) is not null;

        if (underlying == typeof(bool) && nullable)
        {
            var check = new CheckBox { Width = 190, ThreeState = true };

            check.CheckState = field.GetValue(record) switch
            {
                true => CheckState.Checked,
                false => CheckState.Unchecked,
                _ => CheckState.Indeterminate,
            };
            check.CheckStateChanged += (_, _) => field.SetValue(record, check.CheckState switch
            {
                CheckState.Checked => true,
                CheckState.Unchecked => false,
                _ => (bool?)null,
            });

            return check;
        }

        if (underlying == typeof(DateTime) && nullable)
        {
            var current = field.GetValue(record) as DateTime?;
            var picker = new DateTimePicker
            {
                Width = 190,
                Format = DateTimePickerFormat.Short,
                ShowCheckBox = true,
                Value = current ?? DateTime.Today,
                Checked = current.HasValue,
            };

            picker.ValueChanged += (_, _) =>
                field.SetValue(record, picker.Checked ? picker.Value.Date : (DateTime?)null);

            return picker;
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

        // An emptied box is "none" for a nullable number, not a value the binding cannot convert
        // (its default DataSourceNullValue is DBNull, which an int? refuses, keeping the old value).
        box.DataBindings.Add(nullable
            ? new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged)
            {
                NullValue = string.Empty,
                DataSourceNullValue = null,
            }
            : new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

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

        // Fill, not an AutoSize Left: the key strip takes its width first and the titles get what
        // is left. Sized to themselves, they overlapped the strip at the default window width and
        // hid the first key field's label. Fixed-height lines that shorten with an ellipsis, not a
        // flow of AutoSize labels: squeezed, a flow wrapped the heading letter by letter.
        var titles = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 0, 0) };

        var subtitle = heading.Length == 0
            ? string.Empty
            : $"Create and manage {heading.ToLowerInvariant()} records";

        // The bottom line is added first: docking starts from the back of the z-order, so the
        // Dock-Top control added LAST is the one placed highest.
        titles.Controls.Add(new Label
        {
            Text = subtitle,
            ForeColor = Color.White,
            Dock = DockStyle.Top,
            Height = 20,
            AutoEllipsis = true,
        });

        titles.Controls.Add(new Label
        {
            Text = heading,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30,
            AutoEllipsis = true,
        });

        band.Controls.Add(titles);

        if (record is not null)
            band.Controls.Add(BuildKeyStrip(record));

        // Docking lays out from the back of the z-order forward, and a Fill control has to go
        // last to be given only the space the others leave.
        titles.BringToFront();

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

            var editor = BuildFieldEditor(record, field);
            editor.Width = KeyFieldWidth;
            cell.Controls.Add(editor);

            strip.Controls.Add(cell);
        }

        return strip;
    }

    /// <summary>
    /// A key field's editor width: narrower than the form's, whose 190 left three of them taking
    /// about 700 pixels of the band and the heading beside them shortened to "Or...".
    /// </summary>
    private const int KeyFieldWidth = 140;

    /// <summary>
    /// The Additional Information tab: the long-text fields, as multi-line boxes.
    /// </summary>
    /// <remarks>
    /// Returns the page it added, or null when the record has no long-text fields: a caller whose
    /// record can change under it -- <see cref="BuildEntry"/>, when Editor is replaced -- has to be
    /// able to remove and dispose the page bound to the OLD record before adding a fresh one, or the
    /// tab keeps writing into a draft nothing else references any more.
    /// </remarks>
    private TabPage? AddAdditionalTab(TabControl tabs, object record)
    {
        var fields = FieldGroups.Additional(record.GetType());

        if (fields.Count == 0)
            return null;

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
                Margin = new Padding(0, 0, 22, 10),
                Tag = field,
            };

            box.DataBindings.Add(
                new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            body.Controls.Add(box);
        }

        // The emitted constant, not the string again: unlike the WPF layout -- which writes this
        // heading into XAML, where the constant is reachable only through an x:Static reference and
        // an extra xmlns -- this tab is built in code, so it can simply call it. Spelling the
        // heading twice in one shipped app is exactly the drift FieldGroups.AdditionalTitle exists
        // to prevent.
        var page = new TabPage(FieldGroups.AdditionalTitle);

        page.Controls.Add(body);
        tabs.TabPages.Add(page);

        return page;
    }

    /// <summary>
    /// Marks every field control under <paramref name="roots"/> with what the editor's Errors say
    /// against its property, and clears the mark from every field they no longer name.
    /// </summary>
    /// <remarks>
    /// Found by the PropertyInfo each field control carries in its Tag, so the same walk serves a
    /// master's form, its header's key strip, the Additional Information tab and a transaction's
    /// editor. Errors is read by reflection for the reason HasAdditional is: neither interface a
    /// screen is known by here declares it.
    /// </remarks>
    private void PaintErrors(object? editor, params Control?[] roots)
    {
        var errors = editor?.GetType().GetProperty("Errors")?.GetValue(editor)
            as IReadOnlyDictionary<string, string>;

        foreach (var root in roots)
        {
            if (root is not null)
                Mark(root, errors);
        }
    }

    private void Mark(Control parent, IReadOnlyDictionary<string, string>? errors)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.Tag is PropertyInfo field)
            {
                _fieldErrors.SetError(
                    control,
                    errors is not null && errors.TryGetValue(field.Name, out var message) ? message : string.Empty);
            }

            Mark(control, errors);
        }
    }

    /// <summary>The rows a lookup offers, from the pane that holds that entity. Empty when the
    /// application has no pane for it, which is what a line type gets.</summary>
    /// <remarks>
    /// Reads AllRecords rather than Items: Items is the search-filtered list a screen shows, so a
    /// lookup built on it would silently drop every row that does not match text left in that
    /// screen's own search box -- a supplier the user is mid-search on elsewhere vanishing from a
    /// purchase order's supplier picker, with no indication why.
    /// <para>
    /// A List&lt;entityType&gt; and not a List&lt;object&gt;. WinForms resolves DisplayMember and
    /// ValueMember against the list's ITEM TYPE, and object has no properties at all -- so a
    /// List&lt;object&gt; binds a picker that can show no name and return no value, whatever the
    /// rows in it happen to be, and an EMPTY one has nothing to infer a type from either. Building
    /// the list typed is what lets FieldGroups.LookupLabel name a property that will actually
    /// resolve.
    /// </para>
    /// </remarks>
    private IList LookupRows(Type entityType)
    {
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entityType))!;

        foreach (var group in _shell.Groups)
        {
            foreach (var entity in group.Entities)
            {
                if (!string.Equals(entity.Title, entityType.Name, StringComparison.Ordinal))
                    continue;

                if (entity.GetType().GetProperty("AllRecords")?.GetValue(entity) is IEnumerable all)
                {
                    foreach (var row in all)
                        rows.Add(row);
                }

                return rows;
            }
        }

        return rows;
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

        // One row, sized to its buttons. Dock-Fill with wrapping on, in this AutoSize column, was
        // given too little width: a master's six buttons broke onto a second row that the bar's
        // 40-pixel row cut off, and Save and Delete could not be reached even maximised. Left to
        // right so it reads in the order given -- New, Edit, Delete, Save, Clear, Cancel -- as the
        // WPF shell's bar does.
        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

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
    /// <para>
    /// <c>Totals</c> is COMPUTED from the lines, so nothing about it can be data-bound to a Label:
    /// the numbers are painted, and the only thing that says they are stale is the editor raising
    /// <c>PropertyChanged</c> for it -- which it does whenever a line is added, removed, cleared or
    /// edited. Painting once and never listening leaves the most visible numbers on a document
    /// silently wrong, which is what WPF's <c>ItemsSource="{Binding Totals}"</c> avoids by being a
    /// binding. Unlike a master's, this editor's <c>Draft</c> is never replaced, so the form above
    /// needs no repaint of its own.
    /// </para>
    /// </remarks>
    private Control BuildEditor(IRecordEditor editor)
    {
        var draft = editor.GetType().GetProperty("Draft")?.GetValue(editor);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, AutoScroll = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        KeepTabStripOnScreen(layout);

        layout.Controls.Add(BuildEntryHeader(editor, draft), 0, 0);

        if (draft is not null)
            layout.Controls.Add(BuildEntryForm(draft), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        TableLayoutPanel? linesHost = null;
        Control? totals = null;

        // Rebuilds the band beside the grid from the editor's CURRENT totals. Called for the first
        // paint and again on every Totals change -- see the remarks above.
        void RepaintTotals()
        {
            if (linesHost is null)
                return;

            if (totals is not null)
            {
                linesHost.Controls.Remove(totals);
                totals.Dispose();
            }

            totals = BuildTotals(editor);
            linesHost.Controls.Add(totals, 1, 0);
        }

        // A row for the lines only when there are lines: a master's own editor has none, and a
        // tab with an empty grid under it would be worse than no tab at all.
        if (editor.Lines.Count > 0)
        {
            var linesPage = new TabPage(editor.Title);

            linesHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            linesHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            linesHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));

            linesHost.Controls.Add(BuildLines(editor), 0, 0);
            RepaintTotals();

            linesPage.Controls.Add(linesHost);
            tabs.TabPages.Add(linesPage);
        }

        if (draft is not null && HasAdditional(editor))
            AddAdditionalTab(tabs, draft);

        layout.Controls.Add(tabs, 0, 2);

        var bar = BuildEntryActionBar(editor, ("SaveCommand", "Save"), ("CancelCommand", "Cancel"));

        layout.Controls.Add(bar, 0, 3);

        ShowStatus(bar, editor);

        // Two things this screen shows that a one-shot paint cannot: the totals under a line that
        // has just been typed into, and the reason a Save was refused -- a validator's message, or
        // "That record no longer exists.", neither of which the editor reports any other way.
        if (editor is INotifyPropertyChanged notify)
        {
            PropertyChangedEventHandler handler = (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case "Totals":
                        RepaintTotals();
                        break;

                    case "Status":
                        ShowStatus(bar, editor);
                        break;

                    case "Errors":
                        PaintErrors(editor, layout);
                        break;
                }
            };

            notify.PropertyChanged += handler;

            // Unsubscribed when Show discards this screen's control tree, or every rail click
            // leaves another handler on the editor this form no longer shows.
            layout.Disposed += (_, _) => notify.PropertyChanged -= handler;
        }

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

    /// <summary>The least height the tab strip under an entry form is ever given.</summary>
    private const float TabStripFloor = 260f;

    /// <summary>
    /// Keeps an entry screen's tab strip -- the record list, or the lines and their totals --
    /// at a usable height however tall the form above it is.
    /// </summary>
    /// <remarks>
    /// The strip's row is Percent, and a Percent row gets what is left of the panel's CLIENT
    /// height, not of its scrolled extent: AutoScroll alone still hands it zero once the form is
    /// taller than the window, and the list simply is not there. So while the spare height is
    /// below the floor the row is a fixed floor instead, which makes the panel taller than the
    /// window and lets it scroll; once the window has room it goes back to filling it. Checked on
    /// every resize, and whenever a control is added -- a master rebuilds its form on every
    /// record it opens.
    /// </remarks>
    private static void KeepTabStripOnScreen(TableLayoutPanel layout)
    {
        void Fit()
        {
            var form = layout.GetControlFromPosition(0, 1);
            var formHeight = form is null
                ? 0
                : form.GetPreferredSize(new Size(layout.ClientSize.Width, 0)).Height + form.Margin.Vertical;

            var spare = layout.ClientSize.Height - layout.RowStyles[0].Height - formHeight - layout.RowStyles[3].Height;
            var wanted = spare >= TabStripFloor
                ? new RowStyle(SizeType.Percent, 100f)
                : new RowStyle(SizeType.Absolute, TabStripFloor);

            if (layout.RowStyles[2].SizeType != wanted.SizeType)
                layout.RowStyles[2] = wanted;
        }

        layout.Resize += (_, _) => Fit();
        layout.ControlAdded += (_, _) => Fit();
        Fit();
    }

    /// <summary>
    /// Writes the owner's Status onto the status label of the bar it was built with.
    /// </summary>
    /// <remarks>
    /// The label BuildEntryActionBar places has nothing bound to it, and cannot have: it is built
    /// before the owner is known to notify at all, and the owner arrives here as an object rather
    /// than as a screen this file can name. Without this, every message a screen's own commands
    /// report is raised into a line that stays permanently blank -- including the ones that say a
    /// save did NOT happen, which the user has no other way of finding out.
    /// </remarks>
    private static void ShowStatus(Control bar, object owner)
    {
        if (bar.Controls.Find("Status", true).FirstOrDefault() is Label status)
            status.Text = owner.GetType().GetProperty("Status")?.GetValue(owner) as string ?? string.Empty;
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
