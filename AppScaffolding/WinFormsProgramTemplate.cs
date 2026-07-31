namespace AppScaffolding;

/// <summary>The WinForms entry point. Overwrites the template's own Program.cs.</summary>
public static class WinFormsProgramTemplate
{
    /// <summary>Source of <c>Program.cs</c>.</summary>
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
}
