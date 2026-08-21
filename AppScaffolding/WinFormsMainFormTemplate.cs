namespace AppScaffolding;

/// <summary>
/// The WinForms view. Entirely static, like its WPF counterpart: a TabControl over
/// IEntityViewModel renders any entity set, so no entity name ever appears here.
/// </summary>
public static class WinFormsMainFormTemplate
{
    /// <summary>Source of <c>Views/MainForm.cs</c>.</summary>
    /// <remarks>
    /// The <c>IsDisplayable</c> / <c>IsEntityType</c> / <c>DisplayMember</c> helpers emitted here
    /// are deliberately duplicated in <see cref="MainWindowTemplate.CodeBehind"/> and, for
    /// <c>IsDisplayable</c> alone, in <see cref="PropertyEditorTemplate.Source"/> rather than
    /// shared: keep all three in sync if the display convention ever changes.
    /// </remarks>
    public static string Source(string rootNamespace) => Source(rootNamespace, rootNamespace);

    /// <summary>Source of <c>Views/MainForm.cs</c>, titled <paramref name="title"/>.</summary>
    public static string Source(string rootNamespace, string title) => Source(rootNamespace, title, palette: null);

    /// <summary>Source of <c>Views/MainForm.cs</c>, titled <paramref name="title"/>; when
    /// <paramref name="palette"/> is non-null, painted in its colours, and when
    /// <paramref name="iconFileName"/> names a <c>.ico</c>, showing it as the form's icon.</summary>
    /// <remarks>
    /// <c>Form.Icon</c> is an <see cref="System.Drawing.Icon"/>, so only a <c>.ico</c> is loaded:
    /// converting a PNG would mean a bitmap handle for the form to own and free. A non-.ico is
    /// still copied into the project, and still reaches the window on the WPF side.
    /// </remarks>
    public static string Source(
        string rootNamespace, string title, ThemePalette? palette, string? iconFileName = null,
        bool login = false)
    {
        // FindForm() rather than a captured reference: BuildTab is static, and the button is
        // already in the form's control tree by the time anyone can click it. Close() as well
        // as the flag, because the entry point's loop only regains control when the shell
        // closes -- the flag alone would read as a dead button.
        var signOutRow = login
            ? """

        var signOut = new Button { Text = "Sign out", Width = 88 };
        signOut.Click += (s, _) =>
        {
            Session.RequestSignOut();
            ((Control)s!).FindForm()?.Close();
        };

        var signedIn = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = model.SignedInUser,
        };

        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
        };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.Controls.Add(signedIn, 0, 0);
        identity.Controls.Add(signOut, 1, 0);
"""
            : string.Empty;

        // Both carry their own leading newline and both sit at the END of the line before them.
        // A placeholder alone on a line emits a BLANK line when it is empty, which is how the
        // `layout tabs nologin` output drifted one line from what it has always been -- found by
        // diffing a generated app against the same app generated from main.
        var signOutRowStyle = login
            ? "\r\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));"
            : string.Empty;

        // Row 3, which is why rowCount grows with it. Without this the identity panel is built
        // and never added: the button exists, satisfies every assertion that greps the source for
        // it, and is invisible in the running application.
        var signOutPlacement = login
            ? "\r\n        layout.Controls.Add(identity, 0, 3);"
            : string.Empty;

        var authUsing = login ? $"using {rootNamespace}.Auth;\r\n" : string.Empty;

        var rowCount = login ? 4 : 3;

        // Read off disk beside the binary rather than embedded: AppScaffolder registers the copy
        // as content copied to the output directory. Guarded, so an asset the user later deletes
        // costs the app its icon rather than its startup. Types are qualified because the
        // un-iconed form must stay byte-identical, and that rules out adding a using directive.
        var formIcon = iconFileName is not null
                       && iconFileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? $$"""

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "{{AppCustomization.AssetsFolder}}", "{{iconFileName}}");
        if (System.IO.File.Exists(iconPath))
            Icon = new Icon(iconPath);
"""
            : string.Empty;

        // Emitted only for a themed app: an unthemed form must stay byte-identical to the one
        // this generator has always produced.
        var formColours = palette is not null
            ? $$"""

        BackColor = ColorTranslator.FromHtml("{{palette.Background}}");
        ForeColor = ColorTranslator.FromHtml("{{palette.Foreground}}");
"""
            : string.Empty;

        var gridColours = palette is not null
            ? $$"""

        grid.BackgroundColor = ColorTranslator.FromHtml("{{palette.Surface}}");
        grid.DefaultCellStyle.BackColor = ColorTranslator.FromHtml("{{palette.Surface}}");
        grid.DefaultCellStyle.ForeColor = ColorTranslator.FromHtml("{{palette.Foreground}}");
        grid.GridColor = ColorTranslator.FromHtml("{{palette.GridLine}}");
"""
            : string.Empty;

        return $$"""
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
{{authUsing}}using {{rootNamespace}}.ViewModels;

namespace {{rootNamespace}}.Views;

/// <summary>One tab per discovered entity, over the shared view models.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();

    public MainForm()
    {
        Text = "{{title}}";
        Width = 900;
        Height = 600;
{{formColours}}{{formIcon}}
        var tabs = new TabControl { Dock = DockStyle.Fill };

        foreach (var entity in _model.Entities)
            tabs.TabPages.Add(BuildTab(entity, _model));

        Controls.Add(tabs);
    }

    // A TableLayoutPanel rather than three docked controls: docking several controls to the same
    // edge depends on z-order, which is easy to get subtly wrong, and explicit rows cannot.
    private static TabPage BuildTab(IEntityViewModel entity, MainViewModel model)
    {
        var page = new TabPage(entity.Title);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = {{rowCount}} };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));{{signOutRowStyle}}{{signOutRow}}

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
{{gridColours}}
        // DataGridView does not listen to INotifyCollectionChanged, so the collection is
        // republished through a BindingSource whenever the view model adds or removes a record.
        // Cell edits need no help: the bound objects are the view model's own instances.
        var binding = new BindingSource { DataSource = entity.Items };
        grid.DataSource = binding;

        if (entity.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => binding.ResetBindings(false);

        // DataBindingComplete fires more than once during ordinary startup: once when the
        // BindingSource first populates the grid, and again once the grid is actually realized,
        // at which point WinForms' own AutoGenerateColumns machinery re-adds a column for any
        // bound property that has no matching column -- including one this method just removed.
        // So the rules must run on every fire, and must therefore be idempotent.
        grid.DataBindingComplete += (_, _) => ApplyColumnRules(grid, model);

        var add = new Button { Text = "Add", Width = 80 };
        var delete = new Button { Text = "Delete", Width = 80, Enabled = false };
        var save = new Button { Text = "Save", Width = 80 };

        // Right-to-left flow, so adding Save first puts it at the right-hand edge and the row
        // reads Add / Delete / Save, as the WPF window does.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(add);

        var status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = entity.Status,
        };

        // No CommandManager outside WPF, so re-evaluating CanExecute is the view's job.
        grid.SelectionChanged += (_, _) =>
        {
            entity.Selected = grid.CurrentRow?.DataBoundItem;
            delete.Enabled = entity.DeleteCommand.CanExecute(null);
        };

        add.Click += (_, _) => entity.AddCommand.Execute(null);
        delete.Click += (_, _) => entity.DeleteCommand.Execute(null);
        save.Click += (_, _) => entity.SaveCommand.Execute(null);

        // Status is the only thing on the page that changes without a click of its own.
        if (entity is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Status")
                    status.Text = entity.Status;
            };
        }

        layout.Controls.Add(grid, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(status, 0, 2);{{signOutPlacement}}

        page.Controls.Add(layout);
        return page;
    }

    // The same two rules the WPF code-behind applies while generating columns. DataGridView has
    // no per-column event, so the generated set is adjusted afterwards instead.
    private static void ApplyColumnRules(DataGridView grid, MainViewModel model)
    {
        var itemType = ItemType(grid);

        if (itemType is null)
            return;

        for (var i = grid.Columns.Count - 1; i >= 0; i--)
        {
            var column = grid.Columns[i];
            var property = itemType.GetProperty(column.DataPropertyName);

            if (property is null)
                continue;

            // Id is generated, so it is shown but never edited. Setting ReadOnly again on a later
            // pass is harmless, so no guard is needed here.
            if (string.Equals(column.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                column.ReadOnly = true;
                continue;
            }

            // Already swapped to a combo box on an earlier pass: leave it alone rather than
            // tearing it down and rebuilding it on every DataBindingComplete.
            if (column is DataGridViewComboBoxColumn)
                continue;

            // A foreign key is shown as a combo box over the related entity's rows, so a user
            // picks a Customer rather than typing its id.
            if (TryReplaceWithRelationshipColumn(grid, i, itemType, model))
                continue;

            // A navigation property has no sensible column: it would render as its type name, or
            // as "System.Collections.Generic.List`1[Payment]". The foreign key beside it is the
            // editable, meaningful one.
            if (!IsDisplayable(property.PropertyType))
                grid.Columns.RemoveAt(i);
        }
    }

    private static bool TryReplaceWithRelationshipColumn(
        DataGridView grid, int index, Type itemType, MainViewModel model)
    {
        var name = grid.Columns[index].DataPropertyName;

        if (name.Length <= 2 || !name.EndsWith("Id", StringComparison.Ordinal))
            return false;

        if (itemType.GetProperty(name)?.PropertyType != typeof(int))
            return false;

        var navName = name.Substring(0, name.Length - 2);
        var nav = itemType.GetProperty(navName);

        if (nav is null || !IsEntityType(nav.PropertyType))
            return false;

        var tab = model.Entities.FirstOrDefault(vm => vm.Title == navName);

        if (tab is null)
            return false;

        var combo = new DataGridViewComboBoxColumn
        {
            HeaderText = navName,
            DataPropertyName = name,
            DataSource = tab.Items,
            ValueMember = "Id",
            DisplayMember = DisplayMember(nav.PropertyType),
        };

        grid.Columns.RemoveAt(index);
        grid.Columns.Insert(index, combo);
        return true;
    }

    // ObservableCollection<T> arrives here as the BindingSource's DataSource.
    private static Type? ItemType(DataGridView grid)
    {
        var source = (grid.DataSource as BindingSource)?.DataSource?.GetType();

        return source is not null && source.IsGenericType
            ? source.GetGenericArguments()[0]
            : null;
    }

    // An entity type is one of the app's own model classes: a reference type that is neither a
    // string nor a generic collection. (Navigation collections are dropped by IsDisplayable.)
    private static bool IsEntityType(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsGenericType;

    // The same display convention the console picker uses: a Name or Title, else the first
    // string field, else the Id.
    private static string DisplayMember(Type type)
    {
        var display = type.GetProperty("Name") ?? type.GetProperty("Title")
            ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));

        return display?.Name ?? "Id";
    }

    private static bool IsDisplayable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(Guid);
    }
}
""";
    }
}
