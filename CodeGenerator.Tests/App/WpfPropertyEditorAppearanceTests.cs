using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>The emitted edit form's sizing, which is what stops it stretching the window.</summary>
[TestClass]
public class WpfPropertyEditorAppearanceTests
{
    private static string Source() => PropertyEditorTemplate.Source("Payroll");

    [TestMethod]
    public void A_field_is_width_capped_and_left_aligned()
    {
        // Left to a star column a text box for a five-character price spans the whole window.
        var source = Source();

        StringAssert.Contains(source, "MaxWidth = ");
        StringAssert.Contains(source, "HorizontalAlignment = HorizontalAlignment.Left");
    }

    [TestMethod]
    public void Rows_are_spaced_rather_than_touching()
    {
        StringAssert.Contains(Source(), "new Thickness(0, 0, 16, 8)");
    }

    [TestMethod]
    public void A_read_only_field_looks_read_only()
    {
        // Id is shown but never editable, and a box that refuses typing while looking editable
        // reads as a bug.
        var source = Source();

        StringAssert.Contains(source, "box.IsReadOnly");
        StringAssert.Contains(source, "box.Background");
    }

    [TestMethod]
    public void It_still_compiles()
    {
        EmittedDesktopSource.AssertCompiles(Source());
    }
}
