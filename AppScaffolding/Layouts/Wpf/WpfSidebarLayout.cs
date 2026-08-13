namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// A navigation list of entities on the left, the selected entity's pane on the right.
/// </summary>
/// <remarks>
/// Needs no view-model change and no new code-behind: the pane follows the ListBox through
/// <c>ElementName</c> binding, so <see cref="MainWindowTemplate.CodeBehind"/> is emitted verbatim
/// — its AutoGeneratingColumn handler is still what locks the Id column and builds the foreign-key
/// combo boxes.
/// </remarks>
public sealed class WpfSidebarLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Sidebar);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Sidebar];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace)),
    ];

    private static string Xaml(LayoutContext context)
    {
        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        var windowBrushes = context.Themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        var listBrushes = context.Themed
            ? "\n                 Background=\"{DynamicResource AppSurfaceBrush}\""
              + "\n                 Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette)}}
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="180" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <!-- SelectedIndex is explicit for the reason the tabbed layout gives: a Selector only
             follows its collection's current item when the ItemsSource is a CollectionView, so
             bound to an ObservableCollection it would open with nothing selected. -->
        <ListBox x:Name="EntityList"
                 Grid.Column="0"
                 ItemsSource="{Binding Entities}"
                 DisplayMemberPath="Title"
                 SelectedIndex="0"{{listBrushes}} />
        <ContentControl Grid.Column="1"
                        Content="{Binding SelectedItem, ElementName=EntityList}"
                        ContentTemplate="{StaticResource EntityPane}" />
    </Grid>
</Window>
""";
    }
}
