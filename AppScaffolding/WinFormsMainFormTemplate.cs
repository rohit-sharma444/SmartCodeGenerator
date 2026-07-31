namespace AppScaffolding;

/// <summary>
/// The WinForms view. Entirely static, like its WPF counterpart: a TabControl over
/// IEntityViewModel renders any entity set, so no entity name ever appears here.
/// </summary>
public static class WinFormsMainFormTemplate
{
    /// <summary>Source of <c>Views/MainForm.cs</c>.</summary>
    public static string Source(string appName) => $$"""
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using {{appName}}.ViewModels;

namespace {{appName}}.Views;

/// <summary>One tab per discovered entity, over the shared view models.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();

    public MainForm()
    {
        Text = "{{appName}}";
        Width = 900;
        Height = 600;

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

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        // DataGridView does not listen to INotifyCollectionChanged, so the collection is
        // republished through a BindingSource whenever the view model adds or removes a record.
        // Cell edits need no help: the bound objects are the view model's own instances.
        var binding = new BindingSource { DataSource = entity.Items };
        grid.DataSource = binding;

        if (entity.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => binding.ResetBindings(false);

        // Columns are generated when the binding completes, and ResetBindings raises it again.
        // The rules are applied once: re-running them would rebuild the combo columns on
        // every add.
        var columnsAdjusted = false;
        grid.DataBindingComplete += (_, _) =>
        {
            if (columnsAdjusted)
                return;

            columnsAdjusted = true;
            ApplyColumnRules(grid, model);
        };

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
        layout.Controls.Add(status, 0, 2);

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

            // Id is generated, so it is shown but never edited.
            if (string.Equals(column.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                column.ReadOnly = true;
                continue;
            }

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
