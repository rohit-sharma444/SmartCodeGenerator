namespace AppScaffolding;

/// <summary>The WinForms entry point. Overwrites the template's own Program.cs.</summary>
public static class WinFormsProgramTemplate
{
    /// <summary>Source of <c>Program.cs</c> for an app with no login.</summary>
    /// <remarks>
    /// Kept verbatim, and kept reachable: this is the exact entry point the generator has always
    /// produced, and `nologin` must still produce it byte for byte.
    /// </remarks>
    public static string Source(string appName) => $$"""
using System;
using System.Windows.Forms;
using {{appName}}.Views;

namespace {{appName}};

internal static class Program
{
    // EnableVisualStyles + SetCompatibleTextRenderingDefault rather than the generated
    // ApplicationConfiguration.Initialize(): that class only exists when the csproj opts into the
    // WinForms source generator, and this pair compiles in any WinForms project.
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
""";

    /// <summary>
    /// Source of <c>Program.cs</c>. With <paramref name="login"/>, Main signs in first and returns
    /// to the login screen when the shell closes on a sign-out.
    /// </summary>
    public static string Source(string appName, bool login)
    {
        if (!login)
            return Source(appName);

        return $$"""
using System;
using System.Windows.Forms;
using {{appName}}.Auth;
using {{appName}}.Views;

namespace {{appName}};

internal static class Program
{
    // EnableVisualStyles + SetCompatibleTextRenderingDefault rather than the generated
    // ApplicationConfiguration.Initialize(): that class only exists when the csproj opts into the
    // WinForms source generator, and this pair compiles in any WinForms project.
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Before the login screen asks for an account, so the first run has one to offer.
        UserSeed.EnsureAdminSeeded();

        while (true)
        {
            // ShowDialog, not Application.Run: running the login as the message loop would end the
            // application the moment it closed, before the shell could open.
            using (var login = new LoginForm())
            {
                if (login.ShowDialog() != DialogResult.OK)
                    return;
            }

            using (var shell = new MainForm())
            {
                shell.ShowDialog();
            }

            // Closing the shell any other way means quit. Only an explicit sign-out loops back.
            if (!Session.SignOutRequested)
                return;

            Session.Clear();
        }
    }
}
""";
    }
}
