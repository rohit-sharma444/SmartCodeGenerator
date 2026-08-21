using AppScaffolding.Screens;

namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The business shell: a grouped navigation rail, a header naming the signed-in user, and a
/// content area that holds whichever screen is showing.
/// </summary>
/// <remarks>
/// The content area is one <c>ContentControl</c> and four keyed <c>DataTemplate</c>s. There is no
/// per-entity XAML: this file is the same length for a model of two entities and a model of twenty.
/// <para>
/// The templates are chosen by a selector rather than by <c>DataType</c>, which the design would
/// have preferred. WPF resolves an implicit template by the value's EXACT type and cannot name an
/// open generic, and every screen here is <c>MasterScreenViewModel&lt;T&gt;</c> or similar — so
/// <c>DataType</c> can address none of them. The selector is four lines of decision and is the only
/// way to key on what these screens actually are.
/// </para>
/// <para>
/// The literal is <c>$$$</c> because several bindings end in <c>}}</c> — a <c>$$</c> raw literal
/// reads that as an interpolation close and fails with CS9007.
/// </para>
/// </remarks>
public sealed class WpfBusinessLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Business);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Business];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", CodeBehind(context.RootNamespace)),
        new("Views", "ScreenTemplateSelector.cs",
            ScreenTemplates.TemplateSelector(context.RootNamespace)),

        // The editor's form is built from the record's own properties, the same control the
        // master-detail layout uses. Emitted here too, because a layout owns the files its views
        // name rather than assuming another layout ran.
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

        // The same neutrals WpfSidebarLayout uses when the app is not themed, so an unthemed
        // business shell looks deliberate rather than unfinished.
        var railFill = context.Themed ? "{DynamicResource AppSurfaceBrush}" : "#F9FAFB";
        var rule = context.Themed ? "{DynamicResource AppGridLineBrush}" : "#E5E7EB";
        var text = context.Themed ? "{DynamicResource AppForegroundBrush}" : "#111827";
        var muted = context.Themed ? "{DynamicResource AppForegroundBrush}" : "#6B7280";

        return $$$"""
<Window x:Class="{{{context.RootNamespace}}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:{{{context.RootNamespace}}}.Views"
        Title="{{{context.TitleAttribute}}}" Height="640" Width="1000"{{{windowIcon}}}{{{windowBrushes}}}>
    <Window.Resources>
        <!-- One template per kind of screen, keyed by the view model's type. This is what lets the
             content area be a single ContentControl with no selector class. -->
        <DataTemplate x:Key="DashboardTemplate">
            <ScrollViewer>
                <ItemsControl ItemsSource="{Binding Groups}" Margin="16">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,18">
                                <TextBlock Text="{Binding Title}"
                                           FontSize="11"
                                           FontWeight="SemiBold"
                                           Foreground="{{{muted}}}"
                                           Margin="0,0,0,8" />
                                <ItemsControl ItemsSource="{Binding Entities}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border BorderBrush="{{{rule}}}"
                                                    BorderThickness="1"
                                                    CornerRadius="4"
                                                    Padding="16,12"
                                                    Margin="0,0,10,10"
                                                    MinWidth="130">
                                                <StackPanel>
                                                    <TextBlock Text="{Binding Count}"
                                                               FontSize="22"
                                                               FontWeight="Bold"
                                                               Foreground="{{{text}}}" />
                                                    <TextBlock Text="{Binding Title}"
                                                               FontSize="11"
                                                               Foreground="{{{muted}}}" />
                                                </StackPanel>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </DataTemplate>

        <DataTemplate x:Key="MasterTemplate">
            <DockPanel Margin="16">
                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                 Width="150" Margin="0,0,8,0" />
                        <Button Content="New" Command="{Binding NewCommand}" Width="70" Margin="4,0" />
                        <Button Content="Open" Command="{Binding OpenCommand}" Width="70" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}" Width="70" Margin="4,0" />
                    </StackPanel>
                </Grid>
                <TextBlock DockPanel.Dock="Bottom" Text="{Binding Status}" Foreground="{{{muted}}}" />
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          IsReadOnly="True"
                          CanUserAddRows="False"
                          MouseDoubleClick="OnRowActivated"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>

        <DataTemplate x:Key="TransactionTemplate">
            <DockPanel Margin="16">
                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                 Width="150" Margin="0,0,8,0" />
                        <Button Content="New" Command="{Binding NewCommand}" Width="70" Margin="4,0" />
                        <Button Content="Open" Command="{Binding OpenCommand}" Width="70" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}" Width="70" Margin="4,0" />
                    </StackPanel>
                </Grid>
                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
                    <TextBlock Text="From" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding From}" Width="120" Margin="6,0,12,0" />
                    <TextBlock Text="To" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding To}" Width="120" Margin="6,0" />
                </StackPanel>
                <ItemsControl DockPanel.Dock="Bottom" ItemsSource="{Binding Totals}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="16,8,0,0">
                                <TextBlock Text="{Binding Column}" Foreground="{{{muted}}}" Margin="0,0,6,0" />
                                <TextBlock Text="{Binding Value}" FontWeight="Bold" Foreground="{{{text}}}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          IsReadOnly="True"
                          CanUserAddRows="False"
                          MouseDoubleClick="OnRowActivated"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>

        <DataTemplate x:Key="EditorTemplate">
            <DockPanel Margin="16">
                <TextBlock DockPanel.Dock="Top"
                           Text="{Binding Title}"
                           FontSize="15"
                           FontWeight="SemiBold"
                           Margin="0,0,0,12" />
                <StackPanel DockPanel.Dock="Bottom"
                            Orientation="Horizontal"
                            HorizontalAlignment="Right"
                            Margin="0,12,0,0">
                    <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
                    <Button Content="Cancel" Command="{Binding CancelCommand}" Width="88" Margin="12,0,4,0" />
                    <Button Content="Save" Command="{Binding SaveCommand}" Width="88" Margin="4,0,0,0" />
                </StackPanel>
                <ScrollViewer>
                    <views:PropertyEditor Record="{Binding Draft}" />
                </ScrollViewer>
            </DockPanel>
        </DataTemplate>
        <views:ScreenTemplateSelector x:Key="ScreenTemplate"
                                      Dashboard="{StaticResource DashboardTemplate}"
                                      Master="{StaticResource MasterTemplate}"
                                      Transaction="{StaticResource TransactionTemplate}"
                                      Editor="{StaticResource EditorTemplate}" />
    </Window.Resources>

    <DockPanel>
        <Border DockPanel.Dock="Top"
                Background="{{{railFill}}}"
                BorderBrush="{{{rule}}}"
                BorderThickness="0,0,0,1"
                Padding="14,8">
            <DockPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <TextBlock Text="{Binding SignedInUser}"
                               VerticalAlignment="Center"
                               Foreground="{{{muted}}}"
                               Margin="0,0,10,0" />
                    <Button Content="Back" Command="{Binding BackCommand}" Width="70" Margin="4,0" />
                    <Button Content="Sign out" Command="{Binding SignOutCommand}" Width="80" Margin="4,0" />
                </StackPanel>
                <TextBlock Text="{{{context.TitleAttribute}}}"
                           FontSize="14"
                           FontWeight="SemiBold"
                           VerticalAlignment="Center"
                           Foreground="{{{text}}}" />
            </DockPanel>
        </Border>

        <Border DockPanel.Dock="Left"
                Width="184"
                Background="{{{railFill}}}"
                BorderBrush="{{{rule}}}"
                BorderThickness="0,0,1,0">
            <ScrollViewer>
                <StackPanel Margin="0,10">
                    <Button Content="Home"
                            Command="{Binding HomeCommand}"
                            HorizontalContentAlignment="Left"
                            Background="Transparent"
                            BorderThickness="0"
                            Padding="14,5" />
                    <ItemsControl ItemsSource="{Binding Groups}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,10,0,0">
                                    <TextBlock Text="{Binding Title}"
                                               FontSize="10"
                                               FontWeight="SemiBold"
                                               Foreground="{{{muted}}}"
                                               Margin="14,0,0,4" />
                                    <ItemsControl ItemsSource="{Binding Entities}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Button Content="{Binding Title}"
                                                        Click="OnNavigate"
                                                        HorizontalContentAlignment="Left"
                                                        Background="Transparent"
                                                        BorderThickness="0"
                                                        Padding="22,4,14,4" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Border>

        <ContentControl Content="{Binding Current}"
                        ContentTemplateSelector="{StaticResource ScreenTemplate}" />
    </DockPanel>
</Window>
""";
    }

    /// <summary>
    /// Source of <c>Views/MainWindow.xaml.cs</c> for the business shell.
    /// </summary>
    /// <remarks>
    /// Rail clicks and row activation are view concerns: the view models know nothing about which
    /// screen is showing. <c>ConfirmDiscard</c> is wired here because the answer is a message box
    /// on this target and a NavigationLock on the web — the shell only knows it has to ask.
    /// </remarks>
    private static string CodeBehind(string rootNamespace)
    {
        // The column rules and the display convention are long, and the business grids need the
        // same ones, so the shared code-behind is reused rather than restated.
        var source = MainWindowTemplate.CodeBehind(rootNamespace, login: true);

        // One blank line, not two: what precedes the splice already ends in a newline.
        var handlers = """

    // The rail's buttons carry the entity view model as their DataContext, so navigation needs no
    // lookup: whatever was clicked IS the screen to show.
    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: { } screen } && DataContext is ShellViewModel shell)
            shell.TryShow(screen);
    }

    // Double-click opens the selected row, which is the same thing the Open button does.
    private void OnRowActivated(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: { } context })
        {
            var open = context.GetType().GetProperty("OpenCommand")?.GetValue(context);

            if (open is System.Windows.Input.ICommand command && command.CanExecute(null))
                command.Execute(null);
        }
    }
""";

        // Spliced in before the closing brace, at member depth, the way the dashboard layout does.
        var lastBrace = source.LastIndexOf('}');

        return source[..lastBrace] + handlers + source[lastBrace..];
    }
}
