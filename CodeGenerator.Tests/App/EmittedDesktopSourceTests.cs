namespace CodeGenerator.Tests.App;

[TestClass]
public class EmittedDesktopSourceTests
{
    [TestMethod]
    public void DesktopReferences_FindsTheWindowsDesktopReferencePack()
    {
        // A harness that silently found nothing would make every compile test in this suite
        // vacuous: the source would "compile" only because nothing was checked.
        Assert.IsNotEmpty(
            EmittedDesktopSource.DesktopReferences,
            "the Microsoft.WindowsDesktop.App.Ref pack was not found — the desktop compile tests "
            + "would be vacuous without it");
    }

    [TestMethod]
    public void AssertCompiles_AcceptsSourceUsingWpfTypes()
    {
        EmittedDesktopSource.AssertCompiles("""
            using System.Windows.Controls;

            namespace Acme.Views;

            public sealed class Probe : ContentControl
            {
                public Probe() => AddChild(new Grid());
            }
            """);
    }

    [TestMethod]
    public void AssertCompiles_AcceptsSourceUsingWinFormsTypes()
    {
        EmittedDesktopSource.AssertCompiles("""
            using System.Windows.Forms;

            namespace Acme.Views;

            public sealed class Probe : Form
            {
                public Probe() => Controls.Add(new PropertyGrid());
            }
            """);
    }

    [TestMethod]
    public void PlainHarness_CannotCompileWpfTypes_WhichIsWhyThisOneExists()
    {
        // The control for the two tests above. If EmittedSource could already do this, this
        // whole harness would be redundant — and if this test ever fails, delete the harness.
        var result = CodeGenerationEngine.Core.Compiler.Compile("""
            using System.Windows.Controls;

            namespace Acme.Views;

            public sealed class Probe : ContentControl { }
            """);

        Assert.IsFalse(
            result.Success,
            "the plain compiler unexpectedly resolved WPF types; EmittedDesktopSource is no longer needed");
    }
}
