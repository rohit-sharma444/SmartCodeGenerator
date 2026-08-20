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
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace, context.Customization.Login)),
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

        // The nav pane's own fill and the rule dividing it from the records: without them a white
        // column against a white pane reads as an accident rather than as navigation.
        var navFill = context.Themed ? "{DynamicResource AppSurfaceBrush}" : "#F9FAFB";
        var navRule = context.Themed ? "{DynamicResource AppGridLineBrush}" : "#E5E7EB";
        var navText = context.Themed ? "{DynamicResource AppForegroundBrush}" : "#111827";

        var listBrushes = context.Themed
            ? "\n                         Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette, login: context.Customization.Login)}}
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Border Grid.Column="0"
                Background="{{navFill}}"
                BorderBrush="{{navRule}}"
                BorderThickness="0,0,1,0">
            <DockPanel>
                <TextBlock DockPanel.Dock="Top"
                           Text="{{context.TitleAttribute}}"
                           Margin="12,14,12,8"
                           FontSize="15"
                           FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis"
                           Foreground="{{navText}}" />
                <!-- SelectedIndex is explicit for the reason the tabbed layout gives: a Selector
                     only follows its collection's current item when the ItemsSource is a
                     CollectionView, so bound to an ObservableCollection it would open with nothing
                     selected. -->
                <ListBox x:Name="EntityList"
                         ItemsSource="{Binding Entities}"
                         DisplayMemberPath="Title"
                         SelectedIndex="0"
                         BorderThickness="0"
                         Background="Transparent"
                         Padding="4"{{listBrushes}} />
            </DockPanel>
        </Border>
        <ContentControl Grid.Column="1"
                        Content="{Binding SelectedItem, ElementName=EntityList}"
                        ContentTemplate="{StaticResource EntityPane}" />
    </Grid>
</Window>
""";
    }
}
