using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits the WPF login window and the entry point that shows it.</summary>
/// <remarks>
/// <c>App.xaml.cs</c> is emitted rather than left as the template's, because the sign-in has to
/// happen before any window opens and <c>StartupUri</c> cannot express that — it opens its window
/// before <c>OnStartup</c> finishes. The matching removal is
/// <see cref="WpfProjectFixup.RemoveStartupUri"/>.
/// </remarks>
public static class WpfLoginTemplate
{
    /// <summary>The brand panel's fill when the app was given no accent.</summary>
    private const string DefaultBrandFill = "#1D4ED8";

    /// <summary>Source of <c>Views/LoginWindow.xaml</c>.</summary>
    public static string Xaml(LayoutContext context)
    {
        var brandFill = context.Customization.Accent ?? DefaultBrandFill;

        // The company when there is one, else the app name: the panel must never be blank, and an
        // unbranded app still has a name worth showing.
        var brandLine = context.Customization.HasBranding
            ? Escape(context.Customization.Company)
            : context.TitleAttribute;

        var subLine = context.Customization.HasBranding ? context.TitleAttribute : string.Empty;

        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        return $$$"""
<Window x:Class="{{{context.RootNamespace}}}.Views.LoginWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{{context.TitleAttribute}}}"
        Height="420" Width="720"
        MinHeight="360" MinWidth="620"
        WindowStartupLocation="CenterScreen"{{{windowIcon}}}>
    <Window.Resources>
        <!-- The framework's own converter, so the error strip needs no emitted converter class. -->
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
    </Window.Resources>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="44*" MinWidth="220" />
            <ColumnDefinition Width="56*" />
        </Grid.ColumnDefinitions>

        <Border Grid.Column="0" Background="{{{brandFill}}}">
            <DockPanel Margin="28,26">
                <TextBlock DockPanel.Dock="Bottom"
                           Text="{{{subLine}}}"
                           Foreground="#CCFFFFFF"
                           FontSize="12"
                           TextWrapping="Wrap" />
                <StackPanel VerticalAlignment="Top">
                    <Border Width="40" Height="40" CornerRadius="8"
                            Background="#38FFFFFF"
                            HorizontalAlignment="Left"
                            Margin="0,0,0,16" />
                    <TextBlock Text="{{{brandLine}}}"
                               Foreground="White"
                               FontSize="22"
                               FontWeight="Bold"
                               TextWrapping="Wrap" />
                </StackPanel>
            </DockPanel>
        </Border>

        <StackPanel Grid.Column="1"
                    VerticalAlignment="Center"
                    Margin="40,0"
                    MaxWidth="280">
            <TextBlock Text="Sign in" FontSize="20" FontWeight="SemiBold" />
            <TextBlock Text="Enter your credentials" Foreground="#6B7280" Margin="0,2,0,18" />

            <!-- Collapsed until a sign-in fails, so the form does not open showing a red strip. -->
            <Border Background="#FEF2F2"
                    BorderBrush="#FECACA"
                    BorderThickness="1"
                    CornerRadius="3"
                    Padding="8,6"
                    Margin="0,0,0,12"
                    Visibility="{Binding HasError, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Text="{Binding Error}" Foreground="#B91C1C" TextWrapping="Wrap" />
            </Border>

            <TextBlock Text="User name" Foreground="#6B7280" FontSize="11" />
            <TextBox x:Name="UserNameBox"
                     Text="{Binding UserName, UpdateSourceTrigger=PropertyChanged}"
                     Padding="6,4"
                     Margin="0,2,0,12" />

            <TextBlock Text="Password" Foreground="#6B7280" FontSize="11" />
            <!-- A PasswordBox, not a TextBox: PasswordBox.Password is deliberately not a
                 DependencyProperty, so the code-behind hands it over on click. Binding it would
                 mean holding the password in a bindable string. -->
            <PasswordBox x:Name="PasswordBox" Padding="6,4" Margin="0,2,0,18" />

            <Button Content="Sign in"
                    IsDefault="True"
                    Click="OnSignIn"
                    Padding="0,7"
                    Background="{{{brandFill}}}"
                    Foreground="White"
                    BorderThickness="0" />

            <TextBlock Text="{Binding Hint}"
                       Foreground="#9CA3AF"
                       FontSize="11"
                       TextWrapping="Wrap"
                       Margin="0,14,0,0" />
        </StackPanel>
    </Grid>
</Window>
""";
    }

    /// <summary>Source of <c>Views/LoginWindow.xaml.cs</c>.</summary>
    public static string CodeBehind(string rootNamespace) => $$"""
using System.Windows;
using {{rootNamespace}}.ViewModels;

namespace {{rootNamespace}}.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _model = new();

    public LoginWindow()
    {
        InitializeComponent();

        DataContext = _model;
        _model.Succeeded += (_, _) => DialogResult = true;

        Loaded += (_, _) => UserNameBox.Focus();
    }

    // The password travels from the PasswordBox to the view model here, at the moment it is used,
    // rather than living on the view model as a bindable string.
    private void OnSignIn(object sender, RoutedEventArgs e)
    {
        _model.Password = PasswordBox.Password;
        _model.SignInCommand.Execute(null);

        // Cleared whether or not it worked: a failed attempt should not leave the password on
        // screen for the next one.
        PasswordBox.Clear();
    }
}
""";

    /// <summary>Source of <c>App.xaml.cs</c> — the sign-in loop.</summary>
    public static string AppCodeBehind(string rootNamespace) => $$"""
using System.Windows;
using {{rootNamespace}}.Auth;
using {{rootNamespace}}.Views;

namespace {{rootNamespace}};

public partial class App : Application
{
    /// <summary>
    /// Signs in, runs the shell, and comes back here when it closes.
    /// </summary>
    /// <remarks>
    /// ShutdownMode is explicit because there is no StartupUri and no window until the sign-in
    /// succeeds: on the default OnLastWindowClose the application would exit the moment the login
    /// window closed, before the shell opened.
    /// </remarks>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Before the login screen asks for an account, so the first run has one to offer.
        UserSeed.EnsureAdminSeeded();

        while (true)
        {
            if (new LoginWindow().ShowDialog() != true)
                break;

            var shell = new MainWindow();
            MainWindow = shell;
            shell.ShowDialog();

            // Closing the shell any other way means quit. Only an explicit sign-out loops back.
            if (!Session.SignOutRequested)
                break;

            Session.Clear();
        }

        Shutdown();
    }
}
""";

    /// <summary>An attribute value: a company name with an ampersand in it would otherwise make
    /// the XAML unparseable.</summary>
    private static string Escape(string value)
        => AppCustomization.XamlAttribute(value);
}
