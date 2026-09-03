namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The three controls an entry screen is built from: the accent header band, the three columns of
/// grouped panels under it, and the Additional Information tab of long-text fields.
/// </summary>
/// <remarks>
/// Built in code rather than XAML for the reason <see cref="PropertyEditorTemplate"/> records: the
/// entry screen lives inside a DataTemplate, where named elements are per-instance and cannot be
/// reached from the window's code-behind at all. Each derives from Grid and takes the record
/// through its own dependency property, because a ContentControl's single Content slot would hold
/// the record instead of the generated form.
/// <para>
/// None of the three names an entity. The whole point of this layout is that it is the same length
/// for a model of two entities and a model of twenty.
/// </para>
/// </remarks>
public static class WpfEntryControls
{
    /// <summary>Source of <c>Views/EntryHeader.cs</c>.</summary>
    public static string Header(LayoutContext context)
    {
        var accent = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";

        return $$"""
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The accent band at the top of an entry screen: a title, a subtitle, and the record's
/// key fields held out of the panels below.</summary>
public sealed class EntryHeader : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryHeader), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(EntryHeader), new PropertyMetadata(string.Empty, OnChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryHeader)d).Build();

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();

        Background = (Brush)new BrushConverter().ConvertFromString("{{accent}}")!;
        Height = 74;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };

        titles.Children.Add(new TextBlock
        {
            Text = Heading,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });

        titles.Children.Add(new TextBlock
        {
            Text = $"Create and manage {Heading.ToLowerInvariant()} records",
            FontSize = 12,
            Foreground = Brushes.White,
            Opacity = 0.85,
        });

        SetColumn(titles, 1);
        Children.Add(titles);

        if (Record is not null)
        {
            var strip = KeyStrip(Record);

            SetColumn(strip, 2);
            Children.Add(strip);
        }
    }

    /// <summary>The code, date and status, which FieldGroups holds out of every panel so they are
    /// not drawn twice on one screen.</summary>
    private UIElement KeyStrip(object record)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        };

        var key = FieldGroups.Key(record.GetType());

        foreach (var property in new[] { key.Code, key.Date, key.Status })
        {
            if (property is null)
                continue;

            var cell = new StackPanel { Margin = new Thickness(14, 0, 0, 0), MinWidth = 110 };

            cell.Children.Add(new TextBlock
            {
                Text = FieldGroups.LabelOf(property),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 3),
            });

            var box = new TextBox { MinWidth = 110 };

            box.SetBinding(
                TextBox.TextProperty,
                new Binding(property.Name) { Source = record, Mode = BindingMode.TwoWay });

            cell.Children.Add(box);
            strip.Children.Add(cell);
        }

        return strip;
    }
}
""";
    }

    /// <summary>Source of <c>Views/EntryForm.cs</c>.</summary>
    public static string Form(LayoutContext context)
    {
        var accent = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";
        var rule = context.Themed ? "{DynamicResource AppGridLineBrush}" : "#E5E7EB";
        var surface = context.Themed ? "{DynamicResource AppSurfaceBrush}" : "#FFFFFF";

        return $$"""
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The grouped panels of an entry screen, laid out in the three columns FieldGroups
/// packs them into.</summary>
public sealed class EntryForm : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryForm), new PropertyMetadata(null, OnRecordChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    private static Brush Brush(string colour)
        => (Brush)new BrushConverter().ConvertFromString(colour)!;

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryForm)d).Build();

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();

        if (Record is null)
            return;

        var columns = FieldGroups.Columns(Record.GetType());

        for (var i = 0; i < columns.Count; i++)
        {
            // An empty column takes no width, so a small model is not two-thirds whitespace.
            if (columns[i].Count == 0)
                continue;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stack = new StackPanel { Margin = new Thickness(6) };

            foreach (var group in columns[i])
                stack.Children.Add(Panel(group));

            SetColumn(stack, ColumnDefinitions.Count - 1);
            Children.Add(stack);
        }
    }

    private UIElement Panel(FieldGroup group)
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("{{accent}}"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var field in group.Fields)
            body.Children.Add(Row(field));

        return new Border
        {
            BorderBrush = Brush("{{rule}}"),
            BorderThickness = new Thickness(1),
            Background = Brush("{{surface}}"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = body,
        };
    }

    private UIElement Row(PropertyInfo field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 108 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        label.Inlines.Add(new System.Windows.Documents.Run(FieldGroups.LabelOf(field)));

        if (RequiredFields.IsRequired(Record!.GetType(), field.Name))
        {
            label.Inlines.Add(new System.Windows.Documents.Run(" *")
            {
                Foreground = Brushes.Red,
                FontWeight = FontWeights.Bold,
            });
        }

        SetColumn(label, 0);
        row.Children.Add(label);

        var editor = Editor(field);

        SetColumn(editor, 1);
        row.Children.Add(editor);

        return row;
    }

    /// <summary>
    /// The control a field is typed into.
    /// </summary>
    /// <remarks>
    /// A foreign key becomes a picker over the entity it points at -- the same convention the grids'
    /// OnAutoGeneratingColumn applies. Rendering it as a text box, or dropping it, would leave no
    /// way to set a document's supplier at all.
    /// </remarks>
    private FrameworkElement Editor(PropertyInfo property)
    {
        var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var binding = new Binding(property.Name) { Source = Record, Mode = BindingMode.TwoWay };

        if (FieldGroups.NavigationFor(property) is { } nav)
        {
            var lookup = new ComboBox { DisplayMemberPath = "Id" };

            lookup.SetBinding(Selector.SelectedValueProperty, binding);
            lookup.SelectedValuePath = "Id";
            lookup.ItemsSource = Rows(nav.PropertyType);

            return lookup;
        }

        if (underlying == typeof(bool))
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };

            check.SetBinding(ToggleButton.IsCheckedProperty, binding);

            return check;
        }

        if (underlying == typeof(DateTime))
        {
            var picker = new DatePicker();

            picker.SetBinding(DatePicker.SelectedDateProperty, binding);

            return picker;
        }

        if (underlying.IsEnum)
        {
            var combo = new ComboBox { ItemsSource = Enum.GetValues(underlying) };

            combo.SetBinding(Selector.SelectedItemProperty, binding);

            return combo;
        }

        var box = new TextBox();

        box.SetBinding(TextBox.TextProperty, binding);

        return box;
    }

    /// <summary>The rows a lookup offers, read from the pane that holds them. Empty when the
    /// application has no pane for that entity, which is what a line type gets.</summary>
    private static IEnumerable Rows(Type entityType)
    {
        var main = Application.Current?.MainWindow?.DataContext;
        var groups = main?.GetType().GetProperty("Groups")?.GetValue(main) as IEnumerable;

        if (groups is null)
            return Array.Empty<object>();

        foreach (var group in groups)
        {
            var entities = group.GetType().GetProperty("Entities")?.GetValue(group) as IEnumerable;

            if (entities is null)
                continue;

            foreach (var entity in entities)
            {
                var title = entity.GetType().GetProperty("Title")?.GetValue(entity) as string;

                if (string.Equals(title, entityType.Name, StringComparison.Ordinal))
                    return entity.GetType().GetProperty("Items")?.GetValue(entity) as IEnumerable
                           ?? Array.Empty<object>();
            }
        }

        return Array.Empty<object>();
    }
}
""";
    }

    /// <summary>Source of <c>Views/EntryNotes.cs</c>.</summary>
    public static string Notes(LayoutContext context) => $$"""
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The Additional Information tab: the long-text fields, as multi-line boxes.</summary>
/// <remarks>
/// A tab rather than a panel, which is what gives that tab -- present in both mockups -- real
/// content, and what gives Terms and Conditions a home without printing the same field twice.
/// </remarks>
public sealed class EntryNotes : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryNotes), new PropertyMetadata(null, OnRecordChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryNotes)d).Build();

    private void Build()
    {
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();

        if (Record is null)
            return;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 120 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;

        foreach (var field in FieldGroups.Additional(Record.GetType()))
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = FieldGroups.LabelOf(field),
                Margin = new Thickness(10, 10, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };

            SetRow(label, row);
            SetColumn(label, 0);
            Children.Add(label);

            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 90,
                Margin = new Thickness(0, 10, 10, 0),
            };

            box.SetBinding(
                TextBox.TextProperty,
                new Binding(field.Name) { Source = Record, Mode = BindingMode.TwoWay });

            SetRow(box, row);
            SetColumn(box, 1);
            Children.Add(box);

            row++;
        }
    }
}
""";
}
