namespace AppScaffolding.Layouts.WinForms;

/// <summary>
/// The per-entity pane the non-tabbed WinForms layouts share.
/// </summary>
/// <remarks>
/// <c>WinFormsMainFormTemplate</c>'s <c>BuildTab</c> returns a <c>TabPage</c>, which a sidebar or a
/// dashboard cannot host, so the pane is re-emitted here as a <c>Panel</c>-returning
/// <c>BuildPane</c>. The tabbed layout keeps its own copy and must not be refactored onto this
/// one: its output is byte-identical to every pre-layout app's. Change both when the pane changes.
/// <para>
/// The body below — the BindingSource republish, the DataBindingComplete idempotence rule and the
/// four column helpers — is copied verbatim from that file rather than paraphrased. A paraphrase
/// here would be a behaviour difference between two layouts of the same app.
/// </para>
/// </remarks>
public static class WinFormsPaneCode
{
    /// <summary>
    /// The <c>BuildPane</c> method plus the column-rule helpers it needs, ready to paste into a
    /// MainForm class body. With <paramref name="withPropertyGrid"/> the pane carries a
    /// PropertyGrid bound to the current row.
    /// </summary>
    public static string BuildPaneMethod(
        ThemePalette? palette, bool withPropertyGrid = false, bool login = false)
    {
        // Into the existing button row rather than a row of its own: the pane's
        // TableLayoutPanel has two different row shapes here, and threading a fourth through
        // both buys nothing a right-aligned button does not. Flow is right-to-left, so adding
        // it first puts it at the far edge, away from Save.
        //
        // FindForm() because this builder is static and the button is in the form's tree by
        // the time it can be clicked; Close() as well as the flag, because the entry point's
        // loop only regains control when the shell closes.
        // At the END of the line before it: a placeholder alone on a line emits a BLANK line when
        // it is empty, which is how the `nologin` output drifted a line from what it has always
        // been. The fragment carries its own leading newline instead.
        var signOut = login
            ? """

            var signOut = new Button { Text = "Sign out", Width = 88 };
            signOut.Click += (s, _) =>
            {
                Session.RequestSignOut();
                ((Control)s!).FindForm()?.Close();
            };
            buttons.Controls.Add(signOut);
"""
            : string.Empty;

        var gridColours = palette is null
            ? string.Empty
            : $$"""

            grid.BackgroundColor = ColorTranslator.FromHtml("{{palette.Surface}}");
            grid.DefaultCellStyle.BackColor = ColorTranslator.FromHtml("{{palette.Surface}}");
            grid.DefaultCellStyle.ForeColor = ColorTranslator.FromHtml("{{palette.Foreground}}");
            grid.GridColor = ColorTranslator.FromHtml("{{palette.GridLine}}");
    """;

        // The detail row takes 40% of the pane, so the grid keeps the larger share and the button
        // and status rows keep the fixed heights they have in the tabbed layout.
        var rowStyles = withPropertyGrid
            ? """
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
    """
            : """
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
    """;

        var detail = withPropertyGrid
            ? """

            // WinForms has a PropertyGrid, so the detail pane needs no reflection code of its own
            // -- unlike WPF, where the same feature needs an emitted PropertyEditor control.
            var detail = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                ToolbarVisible = false,
                PropertySort = PropertySort.NoSort,
            };

            grid.SelectionChanged += (_, _) => detail.SelectedObject = grid.CurrentRow?.DataBoundItem;
    """
            : string.Empty;

        var layoutAdds = withPropertyGrid
            ? """
            layout.Controls.Add(grid, 0, 0);
            layout.Controls.Add(detail, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            layout.Controls.Add(status, 0, 3);
    """
            : """
            layout.Controls.Add(grid, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(status, 0, 2);
    """;

        return $$"""
    // A TableLayoutPanel rather than several docked controls: docking several controls to the same
    // edge depends on z-order, which is easy to get subtly wrong, and explicit rows cannot. A
    // Panel rather than the tabbed layout's TabPage, so a sidebar or a dashboard can host it.
    private static Control BuildPane(IEntityViewModel entity, MainViewModel model)
    {
        var pane = new Panel { Dock = DockStyle.Fill };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = {{(withPropertyGrid ? 4 : 3)}},
        };
{{rowStyles}}

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
{{detail}}
        var add = new Button { Text = "Add", Width = 80 };
        var delete = new Button { Text = "Delete", Width = 80, Enabled = false };
        var save = new Button { Text = "Save", Width = 80 };

        // Right-to-left flow, so adding Save first puts it at the right-hand edge and the row
        // reads Add / Delete / Save, as the WPF window does.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };{{signOut}}
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

        // Status is the only thing in the pane that changes without a click of its own.
        if (entity is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Status")
                    status.Text = entity.Status;
            };
        }

{{layoutAdds}}

        pane.Controls.Add(layout);
        return pane;
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

        var related = model.Entities.FirstOrDefault(vm => vm.Title == navName);

        if (related is null)
            return false;

        var combo = new DataGridViewComboBoxColumn
        {
            HeaderText = navName,
            DataPropertyName = name,
            DataSource = related.Items,
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
""";
    }
}
