namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// Tab chrome, with each pane split into a grid above and an edit form for the selected row below.
/// </summary>
public sealed class WpfMasterDetailLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.MasterDetail);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.MasterDetail];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace, context.Customization.Login)),
        new("Views", PropertyEditorTemplate.FileName,
            PropertyEditorTemplate.Source(context.RootNamespace)),
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

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:{{context.RootNamespace}}.Views"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette, withEditor: true, login: context.Customization.Login)}}
    <!-- SelectedIndex is explicit for the reason the tabbed layout gives. -->
    <TabControl ItemsSource="{Binding Entities}"
                DisplayMemberPath="Title"
                SelectedIndex="0"
                ContentTemplate="{StaticResource EntityPane}" />
</Window>
""";
    }
}
