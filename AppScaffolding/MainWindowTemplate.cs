namespace AppScaffolding;

/// <summary>
/// The view. Entirely static: a TabControl over IEntityViewModel renders any
/// entity set, so no entity name ever appears in XAML.
/// </summary>
public static class MainWindowTemplate
{
    /// <summary>Source of <c>Views/MainWindow.xaml</c>.</summary>
    public static string Xaml(string appName) => Xaml(appName, appName);

    /// <summary>Source of <c>Views/MainWindow.xaml</c>, titled <paramref name="title"/>.</summary>
    public static string Xaml(string appName, string title) => $$"""
<Window x:Class="{{appName}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{title}}" Height="600" Width="900">
    <!-- SelectedIndex is explicit: a Selector only follows the collection's current item
         when its ItemsSource is a CollectionView, so bound to an ObservableCollection it
         would open with no tab selected and a blank content area. -->
    <TabControl ItemsSource="{Binding Entities}" DisplayMemberPath="Title" SelectedIndex="0">
        <TabControl.ContentTemplate>
            <DataTemplate>
                <DockPanel>
                    <StatusBar DockPanel.Dock="Bottom">
                        <TextBlock Text="{Binding Status}" />
                    </StatusBar>
                    <StackPanel DockPanel.Dock="Bottom"
                                Orientation="Horizontal"
                                HorizontalAlignment="Right"
                                Margin="8">
                        <Button Content="Add" Command="{Binding AddCommand}"
                                Width="80" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}"
                                Width="80" Margin="4,0" />
                        <Button Content="Save" Command="{Binding SaveCommand}"
                                Width="80" Margin="4,0" />
                    </StackPanel>
                    <DataGrid ItemsSource="{Binding Items}"
                              SelectedItem="{Binding Selected, Mode=TwoWay}"
                              AutoGenerateColumns="True"
                              CanUserAddRows="False"
                              AutoGeneratingColumn="OnAutoGeneratingColumn" />
                </DockPanel>
            </DataTemplate>
        </TabControl.ContentTemplate>
    </TabControl>
</Window>
""";

    /// <summary>Source of <c>Views/MainWindow.xaml.cs</c>.</summary>
    /// <remarks>
    /// The <c>IsDisplayable</c> / <c>IsEntityType</c> / <c>DisplayMember</c> helpers emitted here
    /// are deliberately duplicated in <see cref="WinFormsMainFormTemplate.Source"/> rather than
    /// shared: keep the two in sync if the display convention ever changes.
    /// </remarks>
    public static string CodeBehind(string appName) => $$"""
using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using {{appName}}.ViewModels;

namespace {{appName}}.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // Column presentation is a view concern, so the Id lock lives here rather
    // than in a view model. This is the only code-behind in the generated app.
    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            e.Column.IsReadOnly = true;

        // A foreign key (int <Nav>Id whose sibling <Nav> is another entity) is shown as a combo
        // box over that entity's rows, so a user picks a Customer rather than typing its id.
        if (TryBuildRelationshipColumn(sender, e))
            return;

        // A navigation property has no sensible column: a DataGrid would render Customer as its
        // type name and List<Payment> as "System.Collections.Generic.List`1[Payment]". The
        // foreign key next to it is the editable, meaningful one.
        if (!IsDisplayable(e.PropertyType))
            e.Cancel = true;
    }

    private bool TryBuildRelationshipColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyType != typeof(int)
            || !e.PropertyName.EndsWith("Id", StringComparison.Ordinal)
            || e.PropertyName.Length <= 2)
            return false;

        if (sender is not DataGrid grid
            || grid.ItemsSource?.GetType() is not Type source
            || !source.IsGenericType)
            return false;

        var itemType = source.GetGenericArguments()[0];
        var navName = e.PropertyName.Substring(0, e.PropertyName.Length - 2);
        var nav = itemType.GetProperty(navName);
        if (nav is null || !IsEntityType(nav.PropertyType))
            return false;

        if (DataContext is not MainViewModel main)
            return false;

        var tab = main.Entities.FirstOrDefault(vm => vm.Title == navName);
        if (tab is null)
            return false;

        e.Column = new DataGridComboBoxColumn
        {
            Header = navName,
            ItemsSource = tab.Items,
            SelectedValuePath = "Id",
            DisplayMemberPath = DisplayMember(nav.PropertyType),
            SelectedValueBinding = new System.Windows.Data.Binding(e.PropertyName),
        };
        return true;
    }

    // An entity type is one of the app's own model classes: a reference type that is neither a
    // string nor a generic collection. (Navigation collections are cancelled by IsDisplayable.)
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
