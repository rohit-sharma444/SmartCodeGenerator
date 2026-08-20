using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits <c>Views/LoginForm.cs</c>: the Windows Forms half of the split-panel login.</summary>
/// <remarks>
/// Built in code rather than through a designer file, like every other emitted form here — a
/// <c>.Designer.cs</c> would have to be kept in step with a <c>.resx</c> this generator does not
/// write.
/// <para>
/// The caption is escaped as a C# string literal and never as XML: it is emitted into
/// <c>Text = "..."</c>, where an <c>&amp;quot;</c> would appear on screen rather than a quote. This
/// is the distinction <see cref="LayoutContext.TitleLiteral"/> exists for.
/// </para>
/// </remarks>
public static class WinFormsLoginTemplate
{
    /// <summary>The brand panel's fill when the app was given no usable accent, as the three
    /// arguments <c>Color.FromArgb</c> takes.</summary>
    private const string DefaultBrandFill = "29, 78, 216";

    /// <summary>Source of <c>Views/LoginForm.cs</c>.</summary>
    public static string Source(LayoutContext context)
    {
        var brandFill = HexToArgb(context.Customization.Accent) ?? DefaultBrandFill;

        var brandLine = context.Customization.HasBranding
            ? Literal(context.Customization.Company)
            : context.TitleLiteral;

        var subLine = context.Customization.HasBranding ? context.TitleLiteral : string.Empty;

        return $$"""
using System;
using System.Drawing;
using System.Windows.Forms;
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>The sign-in dialog. Returns DialogResult.OK once the credentials were accepted.</summary>
public sealed class LoginForm : Form
{
    private readonly LoginViewModel _model = new();
    private readonly TextBox _userName = new();
    private readonly TextBox _password = new();
    private readonly Label _error = new();

    public LoginForm()
    {
        Text = "{{context.TitleLiteral}}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));

        layout.Controls.Add(BrandPanel(), 0, 0);
        layout.Controls.Add(FormPanel(), 1, 0);

        Controls.Add(layout);
    }

    private static Panel BrandPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb({{brandFill}}),
        };

        // Added before the heading so docking puts it at the bottom: Windows Forms fills docked
        // controls in the order they are added.
        panel.Controls.Add(new Label
        {
            Text = "{{subLine}}",
            ForeColor = Color.FromArgb(210, 226, 255),
            AutoSize = false,
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(28, 0, 28, 22),
        });

        panel.Controls.Add(new Label
        {
            Text = "{{brandLine}}",
            ForeColor = Color.White,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 18F, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 120,
            Padding = new Padding(28, 40, 28, 0),
        });

        return panel;
    }

    private Panel FormPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var heading = new Label
        {
            Text = "Sign in",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(40, 60),
        };

        var subheading = new Label
        {
            Text = "Enter your credentials",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(40, 88),
        };

        _error.ForeColor = Color.FromArgb(185, 28, 28);
        _error.AutoSize = false;
        _error.Size = new Size(280, 34);
        _error.Location = new Point(40, 112);
        _error.Visible = false;

        var userLabel = new Label { Text = "User name", AutoSize = true, Location = new Point(40, 150) };
        _userName.Location = new Point(40, 168);
        _userName.Width = 280;

        var passwordLabel = new Label { Text = "Password", AutoSize = true, Location = new Point(40, 200) };
        _password.Location = new Point(40, 218);
        _password.Width = 280;

        // Not PasswordChar: UseSystemPasswordChar takes the character the operating system uses, so
        // the dialog matches every other password box on the machine.
        _password.UseSystemPasswordChar = true;

        var signIn = new Button
        {
            Text = "Sign in",
            Location = new Point(40, 258),
            Size = new Size(280, 32),
        };

        signIn.Click += OnSignIn;

        // AcceptButton is what makes Enter submit; without it the dialog needs the mouse.
        AcceptButton = signIn;

        var hint = new Label
        {
            Text = _model.Hint,
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(280, 34),
            Location = new Point(40, 300),
        };

        panel.Controls.AddRange(
            [heading, subheading, _error, userLabel, _userName, passwordLabel, _password, signIn, hint]);

        return panel;
    }

    private void OnSignIn(object? sender, EventArgs e)
    {
        _model.UserName = _userName.Text;
        _model.Password = _password.Text;
        _model.SignInCommand.Execute(null);

        // Cleared whether or not it worked: a failed attempt should not leave the password on
        // screen for the next one.
        _password.Clear();

        if (_model.Error is null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _error.Text = _model.Error;
        _error.Visible = true;
    }
}
""";
    }

    /// <summary>
    /// A "#RRGGBB" accent as the three arguments <c>Color.FromArgb</c> takes, or null when it is
    /// not one.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess: the caller falls back to the default fill, because emitting a
    /// half-parsed colour would produce source that does not compile.
    /// </remarks>
    private static string? HexToArgb(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            return null;

        try
        {
            return $"{Convert.ToInt32(hex[1..3], 16)}, {Convert.ToInt32(hex[3..5], 16)}, "
                 + $"{Convert.ToInt32(hex[5..7], 16)}";
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A C# string-literal body: a quote or backslash in a company name would otherwise
    /// break out of the literal it is emitted into.</summary>
    private static string Literal(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
}
