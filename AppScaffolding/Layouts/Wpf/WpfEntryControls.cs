using AppScaffolding.Screens;

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
    /// <summary>The file name <see cref="Header"/> emits, alongside its <c>Views/</c> folder --
    /// set once so Task 9's registration and this task's own compile test cannot spell it two
    /// different ways.</summary>
    public const string HeaderFileName = "EntryHeader.cs";

    /// <summary>The file name <see cref="Form"/> emits.</summary>
    public const string FormFileName = "EntryForm.cs";

    /// <summary>The file name <see cref="Notes"/> emits.</summary>
    public const string NotesFileName = "EntryNotes.cs";

    /// <summary>Source of <c>Views/EntryHeader.cs</c>.</summary>
    public static string Header(LayoutContext context)
    {
        // A resource KEY, not a converter input: BrushConverter parses colour strings only, and a
        // DynamicResource markup extension is not one -- it threw FormatException from inside the
        // DP-changed callback the first time a record reached a themed app's entry screen. The
        // emitted code below calls SetResourceReference with this key instead, which is the code
        // equivalent of what the XAML markup extension was reaching for.
        var accentAssign = context.Themed
            ? "element.SetResourceReference(property, \"AppAccentBrush\")"
            : "element.SetValue(property, (Brush)new BrushConverter().ConvertFromString(\"#2563EB\")!)";

        return $$"""
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(IReadOnlyDictionary<string, string>), typeof(EntryHeader),
        new PropertyMetadata(null, OnErrorsChanged));

    /// <summary>The key-field controls by property name, so a refused save can mark them without
    /// rebuilding the strip -- which would throw away whatever the user is typing.</summary>
    private readonly Dictionary<string, Control> _marks = new(System.StringComparer.Ordinal);

    /// <summary>Chrome that never changes once, put here rather than in <see cref="Build"/> so a
    /// header whose Record is never set still draws its band -- the same precedent
    /// <see cref="PropertyEditorTemplate"/>'s constructor sets.</summary>
    public EntryHeader()
    {
        Height = 74;
        SetAccent(this, BackgroundProperty);

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

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

    /// <summary>Why the last save was refused, by property -- the editor's Errors.</summary>
    public IReadOnlyDictionary<string, string>? Errors
    {
        get => (IReadOnlyDictionary<string, string>?)GetValue(ErrorsProperty);
        set => SetValue(ErrorsProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (EntryHeader)d;

        header._marks.Clear();
        header.Build();
        header.Paint();
    }

    private static void OnErrorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryHeader)d).Paint();

    private void Paint()
    {
        foreach (var (property, editor) in _marks)
            EntryForm.Mark(editor, EntryForm.MessageFor(Errors, property));
    }

    private static void SetAccent(FrameworkElement element, DependencyProperty property)
        => {{accentAssign}};

    private void Build()
    {
        Children.Clear();

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };

        titles.Children.Add(new TextBlock
        {
            Text = Heading,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });

        var subtitle = Heading.Length == 0
            ? string.Empty
            : $"Create and manage {Heading.ToLowerInvariant()} records";

        titles.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = Brushes.White,
            Opacity = 0.85,
        });

        SetColumn(titles, 0);
        Children.Add(titles);

        if (Record is not null)
        {
            var strip = KeyStrip(Record);

            SetColumn(strip, 1);
            Children.Add(strip);
        }
    }

    /// <summary>
    /// The code, date and status, which FieldGroups holds out of every panel so they are not drawn
    /// twice on one screen.
    /// </summary>
    /// <remarks>
    /// Built through <see cref="EntryForm.Editor"/> -- the same DatePicker and enum ComboBox
    /// branches an ordinary panel field gets -- rather than a third hand-rolled label-and-text-box
    /// pair that would type a date or a status as free text.
    /// </remarks>
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

            var editor = EntryForm.Editor(property, record);

            if (editor is Control control)
                _marks[property.Name] = control;

            cell.Children.Add(editor);
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
        // Same reasoning as Header's accentAssign: a resource KEY applied through
        // SetResourceReference in the themed branch, never a DynamicResource string handed to
        // BrushConverter.
        var accentAssign = context.Themed
            ? "element.SetResourceReference(property, \"AppAccentBrush\")"
            : "element.SetValue(property, Brush(\"#2563EB\"))";

        var ruleAssign = context.Themed
            ? "element.SetResourceReference(property, \"AppGridLineBrush\")"
            : "element.SetValue(property, Brush(\"#E5E7EB\"))";

        var surfaceAssign = context.Themed
            ? "element.SetResourceReference(property, \"AppSurfaceBrush\")"
            : "element.SetValue(property, Brush(\"#FFFFFF\"))";

        var buildMethod = context.Customization.EffectiveEntryTemplate switch
        {
            EntryTemplate.Classic => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        var stack = new StackPanel { Margin = new Thickness(6) };

                        foreach (var field in FieldGroups.Flat(Record.GetType()))
                            stack.Children.Add(Row(field));

                        SetColumn(stack, 0);
                        Children.Add(stack);
                    }
                """,

            EntryTemplate.TwoColumn => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        var columns = FieldGroups.Columns(Record.GetType(), 2);

                        for (var i = 0; i < columns.Count; i++)
                        {
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
                """,

            EntryTemplate.Tabbed => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        var tabs = new TabControl();

                        foreach (var group in FieldGroups.For(Record.GetType()))
                        {
                            var stack = new StackPanel { Margin = new Thickness(10) };

                            foreach (var field in group.Fields)
                                stack.Children.Add(Row(field));

                            tabs.Items.Add(new TabItem { Header = group.Title, Content = new ScrollViewer { Content = stack } });
                        }

                        Children.Add(tabs);
                    }
                """,

            // Grouped, Split and Ribbon all use the three-column grouped form: Split and Ribbon
            // change composition and chrome respectively, not field arrangement.
            _ => """
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
                """,
        };

        return $$"""
using System;
using System.Collections;
using System.Collections.Generic;
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

    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(IReadOnlyDictionary<string, string>), typeof(EntryForm),
        new PropertyMetadata(null, OnErrorsChanged));

    /// <summary>Each field's control and the line under it that says what is wrong, by property
    /// name, so a refused save can mark them without rebuilding the form -- which would throw away
    /// whatever the user is typing.</summary>
    private readonly Dictionary<string, (Control Editor, TextBlock Message)> _marks = new(StringComparer.Ordinal);

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    /// <summary>
    /// Why the last save was refused, by property -- the editor's Errors.
    /// </summary>
    /// <remarks>
    /// Bound, not read once: the editor replaces the whole set on every save attempt and announces
    /// it, so a field that has been fixed loses its mark on the next Save, and one that is still
    /// wrong keeps it.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Errors
    {
        get => (IReadOnlyDictionary<string, string>?)GetValue(ErrorsProperty);
        set => SetValue(ErrorsProperty, value);
    }

    /// <summary>What <paramref name="errors"/> says against one property, or empty.</summary>
    internal static string MessageFor(IReadOnlyDictionary<string, string>? errors, string property)
        => errors is not null && errors.TryGetValue(property, out var message) ? message : string.Empty;

    /// <summary>
    /// Marks one field's control with why the last save refused it, or clears the mark.
    /// </summary>
    /// <remarks>
    /// Cleared with ClearValue rather than by setting a colour back, so the control returns to
    /// whatever its style -- themed or not -- gave it. Shared with EntryHeader and EntryNotes, so
    /// a refused field looks the same wherever on the screen it sits.
    /// </remarks>
    internal static void Mark(Control editor, string message)
    {
        if (message.Length == 0)
        {
            editor.ClearValue(Control.BorderBrushProperty);
            editor.ClearValue(Control.BorderThicknessProperty);
            editor.ClearValue(ToolTipProperty);
            return;
        }

        editor.BorderBrush = Brush("#DC2626");
        editor.BorderThickness = new Thickness(2);
        editor.ToolTip = message;
    }

    private static Brush Brush(string colour)
        => (Brush)new BrushConverter().ConvertFromString(colour)!;

    private static void SetAccent(FrameworkElement element, DependencyProperty property)
        => {{accentAssign}};

    private static void SetRule(FrameworkElement element, DependencyProperty property)
        => {{ruleAssign}};

    private static void SetSurface(FrameworkElement element, DependencyProperty property)
        => {{surfaceAssign}};

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var form = (EntryForm)d;

        form._marks.Clear();
        form.Build();
        form.Paint();
    }

    private static void OnErrorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryForm)d).Paint();

    private void Paint()
    {
        foreach (var (property, mark) in _marks)
        {
            var message = MessageFor(Errors, property);

            Mark(mark.Editor, message);

            mark.Message.Text = message;
            mark.Message.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

{{buildMethod}}

    private UIElement Panel(FieldGroup group)
    {
        var body = new StackPanel();

        var heading = new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        SetAccent(heading, TextBlock.ForegroundProperty);
        body.Children.Add(heading);

        foreach (var field in group.Fields)
            body.Children.Add(Row(field));

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = body,
        };

        SetRule(border, Border.BorderBrushProperty);
        SetSurface(border, Border.BackgroundProperty);

        return border;
    }

    private UIElement Row(PropertyInfo field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 108 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // A second row for the message, so a refusal appears under the field it is about rather
        // than only in the status line, which names the first problem on the whole form.
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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

        var editor = Editor(field, Record!);

        SetColumn(editor, 1);
        row.Children.Add(editor);

        var message = new TextBlock
        {
            Foreground = Brush("#DC2626"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        SetRow(message, 1);
        SetColumn(message, 1);
        row.Children.Add(message);

        if (editor is Control control)
            _marks[field.Name] = (control, message);

        return row;
    }

    /// <summary>
    /// The control a field is typed into. Shared by <see cref="EntryHeader"/>'s key strip, so the
    /// code, date and status fields held out of every panel get the same DatePicker and enum
    /// ComboBox branches a panel field does, rather than a second hand-rolled text box.
    /// </summary>
    /// <remarks>
    /// A foreign key becomes a picker over the entity it points at -- the same convention the grids'
    /// OnAutoGeneratingColumn applies. Rendering it as a text box, or dropping it, would leave no
    /// way to set a document's supplier at all. Static and given the record explicitly, rather than
    /// reading an instance <c>Record</c> property, is what makes the sharing possible: EntryHeader
    /// has a Record of its own, of a different DependencyProperty, that this method cannot see.
    /// </remarks>
    internal static FrameworkElement Editor(PropertyInfo field, object record)
    {
        var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;

        var binding = new Binding(field.Name)
        {
            Source = record,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        };

        if (FieldGroups.NavigationFor(field) is { } nav)
        {
            // DisplayMemberPath reads whatever a person calls that row -- Id would list suppliers
            // as 1, 2, 3 rather than the name they are known by, which is what this comment always
            // claimed and what falling back to "Id" on a class with no code field actually did.
            // FieldGroups.LookupLabel is that choice, made once for both targets. ItemsSource,
            // DisplayMemberPath and SelectedValuePath are all set before SetBinding, in the object
            // initializer: a Selector cannot resolve a bound SelectedValue against a path or a list
            // that do not exist yet, and attaching the binding first left an existing record's
            // picker blank.
            var lookup = new ComboBox
            {
                DisplayMemberPath = FieldGroups.LookupLabel(nav.PropertyType),
                SelectedValuePath = "Id",
                ItemsSource = Rows(nav.PropertyType),
            };

            lookup.SetBinding(Selector.SelectedValueProperty, binding);

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

    /// <summary>
    /// The rows a lookup offers, read from the pane that holds them, ignoring whatever that
    /// screen's own search box currently narrows. Empty when the application has no pane for that
    /// entity, which is what a line type gets.
    /// </summary>
    /// <remarks>
    /// Reads AllRecords rather than Items: Items is the search-filtered list a screen shows, so a
    /// lookup built on it would silently drop every row that does not match text left in that
    /// screen's own search box -- a supplier the user is mid-search on elsewhere vanishing from a
    /// purchase order's supplier picker, with no indication why.
    /// </remarks>
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
                    return entity.GetType().GetProperty("AllRecords")?.GetValue(entity) as IEnumerable
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
using System.Collections.Generic;
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

    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(IReadOnlyDictionary<string, string>), typeof(EntryNotes),
        new PropertyMetadata(null, OnErrorsChanged));

    /// <summary>The boxes by property name, so a refused save can mark them without rebuilding
    /// the tab.</summary>
    private readonly Dictionary<string, Control> _marks = new(System.StringComparer.Ordinal);

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    /// <summary>Why the last save was refused, by property -- the editor's Errors.</summary>
    public IReadOnlyDictionary<string, string>? Errors
    {
        get => (IReadOnlyDictionary<string, string>?)GetValue(ErrorsProperty);
        set => SetValue(ErrorsProperty, value);
    }

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var notes = (EntryNotes)d;

        notes._marks.Clear();
        notes.Build();
        notes.Paint();
    }

    private static void OnErrorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryNotes)d).Paint();

    private void Paint()
    {
        foreach (var (property, box) in _marks)
            EntryForm.Mark(box, EntryForm.MessageFor(Errors, property));
    }

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
                new Binding(field.Name)
                {
                    Source = Record,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });

            SetRow(box, row);
            SetColumn(box, 1);
            Children.Add(box);

            _marks[field.Name] = box;

            row++;
        }
    }
}
""";
}
