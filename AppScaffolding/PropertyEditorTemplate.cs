namespace AppScaffolding;

/// <summary>
/// Emits <c>Views/PropertyEditor.cs</c>: the master-detail layout's edit form.
/// </summary>
/// <remarks>
/// WPF has no PropertyGrid, so the form is built by reflecting over the selected record. A control
/// rather than code-behind, because the editor sits inside a <c>DataTemplate</c> — named elements
/// there are per-instance and cannot be reached by name from the window's code-behind at all.
/// <para>
/// It derives from <c>Grid</c> and takes the record through its own <c>Record</c> dependency
/// property. A <c>ContentControl</c> does not work: the pane binds the record to the editor, and a
/// ContentControl's single Content slot would then hold the record instead of the grid the editor
/// builds, so the form would never reach the visual tree.
/// </para>
/// <para>
/// The <c>IsDisplayable</c> convention here is a THIRD copy of the one in
/// <see cref="MainWindowTemplate.CodeBehind"/> and <see cref="WinFormsMainFormTemplate.Source"/>.
/// Sharing it is not available: these are emitted files in a generated app, and lifting the helper
/// into a shared emitted type would change the tabbed layout's output, which must stay byte for
/// byte what it is. Keep all three in sync.
/// </para>
/// </remarks>
public static class PropertyEditorTemplate
{
    public const string FileName = "PropertyEditor.cs";

    public static string Source(string appName) => $$"""
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace {{appName}}.Views;

/// <summary>An edit form for whatever record it is given, built from that record's properties.</summary>
public sealed class PropertyEditor : Grid
{
    /// <summary>The record to edit. The pane template binds the entity's selected row to this.</summary>
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record),
        typeof(object),
        typeof(PropertyEditor),
        new PropertyMetadata(null, OnRecordChanged));

    public PropertyEditor()
    {
        // A minimum on the label column so a form of short names ("Id", "Name") does not sit its
        // fields hard against them; the fields then start in the same place whatever the names are.
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    private static void OnRecordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((PropertyEditor)sender).Rebuild(e.NewValue);

    // Rebuilt on every change rather than bound once: each entity type has different properties,
    // and the same editor instance is reused as the grid selection moves between records.
    private void Rebuild(object? record)
    {
        Children.Clear();
        RowDefinitions.Clear();

        if (record is null)
            return;

        var row = 0;

        foreach (var property in record.GetType().GetProperties())
        {
            if (!property.CanRead || !IsDisplayable(property.PropertyType))
                continue;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = property.Name,
                Margin = new Thickness(0, 0, 16, 8),
                VerticalAlignment = VerticalAlignment.Center,
            };

            SetRow(label, row);
            SetColumn(label, 0);
            Children.Add(label);

            // Capped and left-aligned: in a star-width column a text box for a five-character
            // price stretches the whole window.
            var box = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                MinWidth = 180,
                MaxWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            // Id is assigned by the store's NextId(), so it is shown but never edited -- the same
            // rule the window's column handler applies to the grid.
            box.IsReadOnly = !property.CanWrite
                || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase);

            // A box that refuses typing while looking editable reads as a bug, so a read-only one
            // is visibly muted.
            if (box.IsReadOnly)
                box.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));

            // Source is explicit: this editor's DataContext is the entity's view model, inherited
            // from the pane template, so an unsourced binding would look for the record's
            // properties on the view model and find nothing.
            box.SetBinding(TextBox.TextProperty, new Binding(property.Name)
            {
                Source = record,
                Mode = box.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });

            SetRow(box, row);
            SetColumn(box, 1);
            Children.Add(box);

            row++;
        }
    }

    // Kept in step with the same convention in MainWindow.xaml.cs: a navigation property has no
    // sensible editor, and a collection would render as its type name.
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
