namespace AppScaffolding;

/// <summary>
/// The view. Entirely static: a TabControl over IEntityViewModel renders any
/// entity set, so no entity name ever appears in XAML.
/// </summary>
public static class MainWindowTemplate
{
    /// <summary>Source of <c>Views/MainWindow.xaml</c>.</summary>
    public static string Xaml(string appName) => $$"""
<Window x:Class="{{appName}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{appName}}" Height="600" Width="900">
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
    public static string CodeBehind(string appName) => $$"""
using System;
using System.Windows;
using System.Windows.Controls;
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

        // A navigation property has no sensible column: a DataGrid would render Customer as its
        // type name and List<Payment> as "System.Collections.Generic.List`1[Payment]". The
        // foreign key next to it is the editable, meaningful one.
        if (!IsDisplayable(e.PropertyType))
            e.Cancel = true;
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
