namespace AppScaffolding;

/// <summary>
/// The view. Entirely static: a TabControl over IEntityViewModel renders any
/// entity set, so no entity name ever appears in XAML.
/// </summary>
public static class MainWindowTemplate
{
    /// <summary>Source of <c>Views/MainWindow.xaml</c>.</summary>
    public static string Xaml(string rootNamespace) => Xaml(rootNamespace, rootNamespace);

    /// <summary>Source of <c>Views/MainWindow.xaml</c>, titled <paramref name="title"/>.</summary>
    public static string Xaml(string rootNamespace, string title) => Xaml(rootNamespace, title, themed: false);

    /// <summary>Source of <c>Views/MainWindow.xaml</c>, titled <paramref name="title"/>; when
    /// <paramref name="themed"/>, bound to the brushes <c>Views/Theme.xaml</c> declares, and when
    /// <paramref name="iconFileName"/> is non-null, showing that file from
    /// <see cref="AppCustomization.AssetsFolder"/> as its icon.</summary>
    /// <remarks>
    /// The icon is named as a relative pack URI, which resolves only for a file the project
    /// embeds as a <c>Resource</c> — see <c>AppScaffolder.RegisterIcon</c>, which puts it there.
    /// </remarks>
    public static string Xaml(
        string rootNamespace, string title, bool themed = false, string? iconFileName = null,
        bool login = false)
    {
        // Docked above the grid, inside the pane template, so it renders once: only the
        // selected tab's content is realised. The binding walks to the window's DataContext,
        // which is why this literal is $$$ -- it ends in }}.
        var signOut = login
            ? $$$"""

                                <Grid DockPanel.Dock="Top" Margin="0,0,0,8">
                                    <TextBlock VerticalAlignment="Center"
                                               Text="{Binding DataContext.SignedInUser, RelativeSource={RelativeSource AncestorType=Window}}" />
                                    <Button Content="Sign out"
                                            Click="OnSignOut"
                                            HorizontalAlignment="Right"
                                            Width="80" Margin="4,0" />
                                </Grid>
            """
            : string.Empty;

        // Emitted only for an app that was given an icon: the window has always been iconless,
        // and inherits the .exe's icon when there is one.
        var windowIcon = iconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{iconFileName}\"";

        // Emitted only for a themed app: an unthemed window must stay byte-identical to the one
        // this generator has always produced.
        var windowBrushes = themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        var gridBrushes = themed
            ? "\n                              Background=\"{DynamicResource AppSurfaceBrush}\""
              + "\n                              Foreground=\"{DynamicResource AppForegroundBrush}\""
              + "\n                              HorizontalGridLinesBrush=\"{DynamicResource AppGridLineBrush}\""
              + "\n                              VerticalGridLinesBrush=\"{DynamicResource AppGridLineBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{rootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{title}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
    <!-- SelectedIndex is explicit: a Selector only follows the collection's current item
         when its ItemsSource is a CollectionView, so bound to an ObservableCollection it
         would open with no tab selected and a blank content area. -->
    <TabControl ItemsSource="{Binding Entities}" DisplayMemberPath="Title" SelectedIndex="0">
        <TabControl.ContentTemplate>
            <DataTemplate>
                <DockPanel>{{signOut}}
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
                    <DataGrid ItemsSource="{Binding Items}"{{gridBrushes}}
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
    }

    /// <summary>Source of <c>Views/MainWindow.xaml.cs</c>.</summary>
    /// <remarks>
    /// The <c>IsDisplayable</c> / <c>IsEntityType</c> / <c>DisplayMember</c> helpers emitted here
    /// are deliberately duplicated in <see cref="WinFormsMainFormTemplate.Source"/> and, for
    /// <c>IsDisplayable</c> alone, in <see cref="PropertyEditorTemplate.Source"/> rather than
    /// shared: keep all three in sync if the display convention ever changes.
    /// </remarks>
    public static string CodeBehind(string rootNamespace) => CodeBehind(rootNamespace, login: false);

    /// <summary>
    /// Source of <c>Views/MainWindow.xaml.cs</c>. With <paramref name="login"/> it also
    /// handles the pane's Sign out button.
    /// </summary>
    /// <remarks>
    /// The handler closes the window as well as raising the flag. The entry point's loop only
    /// gets control back when the shell closes, so setting the flag alone would do nothing
    /// visible and read as a dead button.
    /// </remarks>
    public static string CodeBehind(string rootNamespace, bool login)
    {
        var signOut = login
            ? """


    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        Session.RequestSignOut();
        Close();
    }
"""
            : string.Empty;

        var authUsing = login ? $"using {rootNamespace}.Auth;\r\n" : string.Empty;

        return $$"""
using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
{{authUsing}}using {{rootNamespace}}.ViewModels;

namespace {{rootNamespace}}.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }{{signOut}}

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
}
