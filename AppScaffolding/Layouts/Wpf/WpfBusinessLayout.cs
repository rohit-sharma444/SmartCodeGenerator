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

        // The three controls both entry screens are built from. A layout owns the files its views
        // name rather than assuming another layout ran, and ALL THREE are registered because both
        // templates name all three: a window referencing a views:EntryNotes that no file declares
        // is a compile error in the generated app rather than a failure in this repository's tests.
        //
        // PropertyEditor is deliberately not among them any more. It is the flat one-column form
        // these screens replace, and emitting it here as well would ship a second editor for the
        // same record that nothing draws. The four pre-business layouts still emit it.
        new("Views", WpfEntryControls.HeaderFileName, WpfEntryControls.Header(context)),
        new("Views", WpfEntryControls.FormFileName, WpfEntryControls.Form(context)),
        new("Views", WpfEntryControls.NotesFileName, WpfEntryControls.Notes(context)),
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

        // The entry screens' accent, the same one WpfEntryControls paints the header band and the
        // panel headings with, so the two halves of one screen do not disagree about it.
        var accentBrush = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";

        // The one string that names the long-text tab, read rather than spelled again here: the
        // emitted FieldGroups.AdditionalTitle the WinForms layout calls is built from the same
        // constant.
        var additionalTitle = FieldGroupsTemplate.AdditionalTitle;

        var isRibbon = context.Customization.EffectiveEntryTemplate == Screens.EntryTemplate.Ribbon;

        // WPF's <Style> can style every Button in a scope without repeating six attributes six
        // times, but a StaticResource style can't be reused across the two DIFFERENT button sets
        // (Master's six commands, the Editor's two) without also being shared, so each bar carries
        // its own inline styling instead -- consistent with how every other brush in this file is
        // assigned, and avoiding a fifth Window.Resources entry for a chrome choice this narrow.
        // Two dollar signs, not one: a raw string literal's literal braces must be a SHORTER run
        // than its dollar count, not a doubled one as in a normal interpolated string, so the
        // single literal brace WPF's binding syntax needs ({Binding ...}) forces the interpolation
        // holes here to open with {{ and close with }}.
        static string RibbonButton(string content, string command, string width = "76")
            => $$"""
                <StackPanel Orientation="Vertical" Margin="3,0" Width="{{width}}">
                    <Button Content="{{content}}" Command="{Binding {{command}}}" Padding="4,6" />
                </StackPanel>
                """;

        return $$$"""
<Window x:Class="{{{context.RootNamespace}}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:{{{context.RootNamespace}}}.Views"
        Title="{{{context.TitleAttribute}}}" Height="640" Width="1000"{{{windowIcon}}}{{{windowBrushes}}}>
    <Window.Resources>
        <!-- The visibility of a whole area, decided by the view model rather than by conditional
             markup: this layout emits no per-entity XAML, so an Additional Information tab and a
             lines area cannot be emitted only for the models that have them. -->
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />

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

        <!-- The master mockup: one screen holding the entry form and the list of what has been
             entered. Selecting a row loads it into the form above, so there is no Open button and
             no double-click handler here. The children are ordered as a DockPanel needs them:
             everything docked first, and the area that should take the leftover space last. -->
        <DataTemplate x:Key="MasterTemplate">
            <DockPanel>
                <views:EntryHeader DockPanel.Dock="Top"
                                   Heading="{Binding Title}"
                                   Record="{Binding Editor.Draft}" />
                <Border DockPanel.Dock="Bottom" Background="{{{railFill}}}" Padding="10,6">
                    <DockPanel>
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
{{{(isRibbon
    ? $"""
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Tag="ribbon">
                            {RibbonButton("New", "NewCommand")}
                            {RibbonButton("Edit", "EditCommand")}
                            {RibbonButton("Delete", "DeleteCommand")}
                            {RibbonButton("Save", "SaveCommand")}
                            {RibbonButton("Clear", "ClearCommand")}
                            {RibbonButton("Cancel", "CancelCommand")}
                        </StackPanel>
""" : """
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="New" Command="{Binding NewCommand}" Width="80" Margin="4,0" />
                            <Button Content="Edit" Command="{Binding EditCommand}" Width="80" Margin="4,0" />
                            <Button Content="Delete" Command="{Binding DeleteCommand}" Width="80" Margin="4,0" />
                            <Button Content="Save" Command="{Binding SaveCommand}" Width="80" Margin="16,0,4,0" />
                            <Button Content="Clear" Command="{Binding ClearCommand}" Width="80" Margin="4,0" />
                            <Button Content="Cancel" Command="{Binding CancelCommand}" Width="80" Margin="4,0" />
                        </StackPanel>
""")}}}
                    </DockPanel>
                </Border>
                <views:EntryForm DockPanel.Dock="Top" Record="{Binding Editor.Draft}" Margin="10" />
                <TabControl Margin="10,0,10,10">
                    <TabItem Header="{Binding Title}">
                        <DockPanel>
                            <DockPanel DockPanel.Dock="Top" Margin="0,6,0,6">
                                <TextBlock DockPanel.Dock="Right"
                                           Text="{Binding Shown, StringFormat='Total Records: {0}'}"
                                           Foreground="{{{muted}}}"
                                           VerticalAlignment="Center" />
                                <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                         Width="240"
                                         HorizontalAlignment="Left" />
                            </DockPanel>
                            <DataGrid ItemsSource="{Binding Items}"
                                      SelectedItem="{Binding Selected, Mode=TwoWay}"
                                      AutoGenerateColumns="True"
                                      IsReadOnly="True"
                                      CanUserAddRows="False"
                                      AutoGeneratingColumn="OnAutoGeneratingColumn" />
                        </DockPanel>
                    </TabItem>

                    <!-- Both of this screen's tabs are static, so the strip can hold one that is
                         not a record list. The transaction screen's cannot: see EditorTemplate. -->
                    <TabItem Header="{{{additionalTitle}}}"
                             Visibility="{Binding HasAdditional, Converter={StaticResource BoolToVisibility}}">
                        <!-- Scrolled, because EntryNotes stacks a 90px box per long-text field and
                             a tab clips whatever does not fit. -->
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <views:EntryNotes Record="{Binding Editor.Draft}" />
                        </ScrollViewer>
                    </TabItem>
                </TabControl>
            </DockPanel>
        </DataTemplate>

        <DataTemplate x:Key="TransactionTemplate">
            <DockPanel Margin="16">
                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" />
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}"
                                   VerticalAlignment="Center" Margin="12,0,0,0" />
                    </StackPanel>
{{{(isRibbon
    // Two dollar signs here too: the search box's own literal "{Binding Search, ...}" needs a
    // single-brace run to survive as text, which only holds while the interpolation holes below
    // open and close with a DOUBLED brace instead.
    ? $$"""
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Tag="ribbon">
                        <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                 Width="150" Margin="0,0,8,0" />
                        {{RibbonButton("New", "NewCommand", "60")}}
                        {{RibbonButton("Open", "OpenCommand", "60")}}
                        {{RibbonButton("Delete", "DeleteCommand", "60")}}
                    </StackPanel>
""" : """
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                 Width="150" Margin="0,0,8,0" />
                        <Button Content="New" Command="{Binding NewCommand}" Width="70" Margin="4,0" />
                        <Button Content="Open" Command="{Binding OpenCommand}" Width="70" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}" Width="70" Margin="4,0" />
                    </StackPanel>
""")}}}
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

        <!-- The transaction mockup: the same header and form as a master, with the document's
             lines and their totals under it. Docked children come first and the lines area last,
             because a DockPanel gives the leftover space to whichever child it holds last. -->
        <DataTemplate x:Key="EditorTemplate">
            <DockPanel>
                <views:EntryHeader DockPanel.Dock="Top"
                                   Heading="{Binding Title}"
                                   Record="{Binding Draft}" />
                <Border DockPanel.Dock="Bottom" Background="{{{railFill}}}" Padding="10,6">
                    <DockPanel>
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
{{{(isRibbon
    ? $"""
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Tag="ribbon">
                            {RibbonButton("Save", "SaveCommand")}
                            {RibbonButton("Cancel", "CancelCommand")}
                        </StackPanel>
""" : """
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="Save" Command="{Binding SaveCommand}" Width="88" Margin="4,0" />
                            <Button Content="Cancel" Command="{Binding CancelCommand}" Width="88" Margin="4,0" />
                        </StackPanel>
""")}}}
                    </DockPanel>
                </Border>
                <views:EntryForm DockPanel.Dock="Top" Record="{Binding Draft}" Margin="10" />

                <!-- A titled section under the lines rather than a tab beside them. The lines tab
                     strip is driven by ItemsSource, and mixing a static TabItem into it needs a
                     CompositeCollection, whose CollectionContainer is not in the visual tree: it
                     has no DataContext inside a DataTemplate and binds nothing, which parses
                     cleanly and renders an empty strip. The master screen keeps a real tab,
                     because both of its tabs are static. -->
                <!-- MaxHeight, and a DockPanel rather than a StackPanel inside it. A docked child
                     takes its whole desired height before the fill child gets any, and EntryNotes
                     asks for 90px per long-text field with no ceiling: three of them plus the band,
                     the form and the action bar exceed this window's 640, and a DockPanel CLIPS
                     what does not fit rather than scrolling it. Uncapped, the lines grid below
                     would be arranged at zero height and the document's lines, which are the whole
                     point of this screen, would silently not be there. The ScrollViewer is the last
                     child of a DockPanel and not of a StackPanel for the same reason: a StackPanel
                     hands a child its full desired height, so a ScrollViewer inside one is clipped
                     instead of scrolling. -->
                <Border DockPanel.Dock="Bottom"
                        BorderBrush="{{{rule}}}"
                        BorderThickness="1"
                        Margin="10,0,10,10"
                        Padding="12,8"
                        MaxHeight="180"
                        Visibility="{Binding HasAdditional, Converter={StaticResource BoolToVisibility}}">
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Top"
                                   Text="{{{additionalTitle}}}"
                                   FontWeight="SemiBold"
                                   Foreground="{{{accentBrush}}}"
                                   Margin="0,0,0,6" />
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <views:EntryNotes Record="{Binding Draft}" />
                        </ScrollViewer>
                    </DockPanel>
                </Border>

                <!-- The totals sit beside the lines, and both are hidden for a master: an editor
                     with no line sets is a form and nothing else, which is what HasLines says. -->
                <Grid Margin="10,0,10,10"
                      Visibility="{Binding HasLines, Converter={StaticResource BoolToVisibility}}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TabControl Grid.Column="0" ItemsSource="{Binding Lines}">
                        <TabControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Title}" />
                            </DataTemplate>
                        </TabControl.ItemTemplate>
                        <TabControl.ContentTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="0,6,0,0">
                                        <Button Content="Add Row" Click="OnAddLine" Width="88" />
                                        <Button Content="Delete Row" Click="OnRemoveLine" Width="96" Margin="6,0,0,0" />
                                        <Button Content="Clear All" Click="OnClearLines" Width="88" Margin="6,0,0,0" />
                                    </StackPanel>
                                    <!-- Typed into, unlike the record lists: this grid IS the
                                         editor for a line, which has no screen of its own. Its
                                         cell edits reach the line object without the set seeing
                                         them, so the commit is reported to the set, which is what
                                         keeps the totals beside it honest. -->
                                    <DataGrid ItemsSource="{Binding Items}"
                                              SelectedItem="{Binding Selected}"
                                              AutoGenerateColumns="True"
                                              AutoGeneratingColumn="OnAutoGeneratingColumn"
                                              CellEditEnding="OnLineCellEdited"
                                              CanUserAddRows="False"
                                              HeadersVisibility="Column"
                                              GridLinesVisibility="Horizontal"
                                              BorderBrush="{{{rule}}}" />
                                </DockPanel>
                            </DataTemplate>
                        </TabControl.ContentTemplate>
                    </TabControl>
                    <Border Grid.Column="1"
                            BorderBrush="{{{rule}}}"
                            BorderThickness="1"
                            Margin="12,0,0,0"
                            Padding="12,8"
                            MinWidth="240"
                            VerticalAlignment="Top">
                        <ItemsControl ItemsSource="{Binding Totals}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <DockPanel Margin="0,3">
                                        <TextBlock Text="{Binding Column}" Foreground="{{{muted}}}" />
                                        <TextBlock Text="{Binding Value, StringFormat=N2}"
                                                   HorizontalAlignment="Right"
                                                   TextAlignment="Right"
                                                   Foreground="{{{text}}}" />
                                    </DockPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                </Grid>
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
                    <Button Content="Sign out" Click="OnShellSignOut" Width="80" Margin="4,0" />
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
    /// <summary>
    /// What the window's DataContext is: the shell, built over the pane list.
    /// </summary>
    /// <remarks>
    /// ConfirmDiscard is wired here because the answer is a message box on this target and a
    /// NavigationLock on the web — the shell only knows that it has to ask.
    /// </remarks>
    private static string Shell() => """
        var model = new MainViewModel();

        var shell = new ShellViewModel(
            model.Entities.Where(e => !IsAdminPane(e)).ToList(),
            model.Entities.Where(IsAdminPane).ToList());

        // The desktop's answer to "discard your changes?". What asking looks like belongs to the
        // view; the shell only knows it has to ask.
        shell.ConfirmDiscard = () => MessageBox.Show(
            this, "Discard your changes?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        DataContext = shell;
""";

    /// <summary>
    /// The first statement of the shared <c>OnAutoGeneratingColumn</c>, which the lines rule is
    /// spliced in front of.
    /// </summary>
    /// <remarks>
    /// In front of it, not after: the shared rules turn a &lt;Nav&gt;Id into a combo box and
    /// <c>return</c>, so a rule that ran later would be reached only for the columns the grid was
    /// never going to draw as a picker anyway.
    /// </remarks>
    private const string LineGridColumnAnchor =
        "        if (string.Equals(e.PropertyName, \"Id\", StringComparison.OrdinalIgnoreCase))";

    /// <summary>
    /// Drops the foreign key from a lines grid.
    /// </summary>
    /// <remarks>
    /// The set on the row knows its own key name, so this needs no entity name and no per-model
    /// emission. Found by opening a generated app: WinForms hid the column and the web filtered
    /// the field out, and WPF drew it as a picker over every other document.
    /// </remarks>
    private const string LineGridColumnRule = """
        // The lines of a document belong to the document being edited: LineSet stamps the parent
        // id on save, so it is not a choice the user makes -- and the rule below would draw it as
        // a combo box over every other document, inviting a line to be moved to one of them.
        if (sender is FrameworkElement { DataContext: ILineSet lines }
            && string.Equals(e.PropertyName, lines.ForeignKey, StringComparison.Ordinal))
        {
            e.Cancel = true;
            return;
        }


""";

    private static string CodeBehind(string rootNamespace)
    {
        // The column rules and the display convention are long, and the business grids need the
        // same ones, so the shared code-behind is reused rather than restated.
        //
        // login: false, then the Auth using is put back by hand. The shared sign-out handler
        // closes the window without asking, which is right for the four older layouts and wrong
        // here -- this shell can be holding an editor with unsaved edits -- so it is replaced
        // rather than left in place as a second handler nothing calls.
        var source = MainWindowTemplate.CodeBehind(rootNamespace, login: false)
            .Replace(
                $"using {rootNamespace}.ViewModels;",
                $"using {rootNamespace}.Auth;\r\nusing {rootNamespace}.ViewModels;",
                StringComparison.Ordinal);

        // The window binds the SHELL, not the pane list every other layout binds. Without this the
        // rail, the content area and the header all bind to properties MainViewModel does not
        // have: the window opens showing a header and nothing else, and SignedInUser resolves only
        // because MainViewModel happens to have one too. Found by signing into a built app.
        source = source.Replace(
            "        DataContext = new MainViewModel();",
            Shell(),
            StringComparison.Ordinal);

        // The line handlers name ILineSet, which lives with the roles rather than the view models.
        source = source.Replace(
            $"using {rootNamespace}.ViewModels;",
            $"using {rootNamespace}.Screens;\r\nusing {rootNamespace}.ViewModels;",
            StringComparison.Ordinal);

        // Spliced rather than added to the shared rules: the four older layouts share that
        // code-behind and emit no Screens/LineSet.cs, so naming ILineSet there would be a compile
        // error in every one of their generated apps.
        source = source.Replace(
            LineGridColumnAnchor, LineGridColumnRule + LineGridColumnAnchor, StringComparison.Ordinal);

        // One blank line, not two: what precedes the splice already ends in a newline.
        var handlers = """

    /// <summary>The Users pane, which belongs under Admin rather than among the entities.</summary>
    private static bool IsAdminPane(IEntityViewModel entity)
        => string.Equals(entity.Title, "Users", StringComparison.Ordinal);

    // The command asks before discarding an edit, and closing is what hands control back to the
    // sign-in loop in App.xaml.cs -- it only regains control when this window closes.
    private void OnShellSignOut(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;

        shell.SignOutCommand.Execute(null);

        if (Session.SignOutRequested)
            Close();
    }

    // The line buttons carry their own ILineSet as their DataContext, the same way the rail's
    // buttons carry a screen: whatever was clicked knows which lines it means.
    private void OnAddLine(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILineSet lines })
            lines.Add();
    }

    private void OnRemoveLine(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILineSet lines })
            lines.Remove(lines.Selected);
    }

    private void OnClearLines(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILineSet lines })
            lines.Clear();
    }

    // A typed value reaches the line object directly, so nothing derived from the lines -- the
    // totals beside them -- hears about it. Telling the set is what makes that band recompute.
    private void OnLineCellEdited(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        // On the next dispatcher pass, not here: CellEditEnding runs BEFORE the cell's binding
        // writes the typed value into the line, so a total recomputed at this point would sum the
        // values the grid is about to replace.
        if (sender is FrameworkElement { DataContext: ILineSet lines })
            Dispatcher.BeginInvoke(new Action(lines.LineEdited));
    }

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
